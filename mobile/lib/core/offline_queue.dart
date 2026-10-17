import 'dart:convert';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../api/records_client.dart';

enum QueueState { pending, synced, failed, conflict }

/// One report made without a connection. It holds only what the server needs to apply it again
/// (ids of the planned dose, the chosen status, when the person made it): no name, note, token or free text.
class QueuedOp {
  QueuedOp({
    required this.id,
    required this.slotKey,
    required this.payload,
    required this.queuedAt,
    this.state = QueueState.pending,
    this.attempts = 0,
    this.code,
  });

  /// Idempotency identity of this local operation (never reused, never sent as a secret).
  final String id;

  /// `scheduleEntryId|scheduledFor`: one live operation per planned dose.
  final String slotKey;
  final Json payload;
  final DateTime queuedAt;
  QueueState state;
  int attempts;
  String? code;

  String get status => '${payload['status']}';

  Json toJson() => {
    'id': id,
    'slotKey': slotKey,
    'payload': payload,
    'queuedAt': queuedAt.toUtc().toIso8601String(),
    'state': state.name,
    'attempts': attempts,
    'code': code,
  };

  static QueuedOp? fromJson(Object? j) {
    if (j is! Map) return null;
    final state = QueueState.values.where((s) => s.name == j['state']).firstOrNull;
    final at = DateTime.tryParse('${j['queuedAt']}');
    final payload = j['payload'];
    if (state == null || at == null || payload is! Map || j['id'] is! String || j['slotKey'] is! String) {
      return null; // a damaged entry is dropped, never guessed at
    }
    return QueuedOp(
      id: j['id'] as String,
      slotKey: j['slotKey'] as String,
      payload: payload.cast<String, dynamic>(),
      queuedAt: at,
      state: state,
      attempts: (j['attempts'] as num?)?.toInt() ?? 0,
      code: j['code'] as String?,
    );
  }
}

enum SubmitResult { synced, queued }

/// Bounded queue of intake reports made while the server could not be reached.
///
/// * Local-only data: kept in the device's preferences (no token, no free text), cleared on sign-out.
/// * A report is `pending` until the server accepts it; it is never shown as saved before that.
/// * The server decides conflicts: if it holds a different, newer record for the same dose the report becomes
///   `conflict` and is never applied over it. The person chooses what to do (keep the server's record = discard).
/// * Retries are bounded: after [maxAttempts] failed attempts the operation is `failed` and waits for the person.
/// * Replays are safe: the server treats the same report for the same dose as a no-op.
class OfflineQueue extends ChangeNotifier {
  OfflineQueue({this.prefs, DateTime Function()? now, this.maxItems = 50, this.maxAttempts = 5})
    : _now = now ?? DateTime.now;

  final SharedPreferences? prefs;
  final DateTime Function() _now;
  final int maxItems;
  final int maxAttempts;
  String? _account;
  final List<QueuedOp> _ops = [];
  bool _syncing = false;

  static const _keyPrefix = 'offlineQueue.v1.';

  bool get syncing => _syncing;
  List<QueuedOp> get ops => List.unmodifiable(_ops);
  int get pendingCount => _ops.where((o) => o.state == QueueState.pending).length;
  int get attentionCount => _ops.where((o) => o.state == QueueState.failed || o.state == QueueState.conflict).length;

  static String slotKeyOf(Json slot) => '${slot['scheduleEntryId']}|${slot['scheduledFor']}';

  QueuedOp? forSlot(Json slot) {
    final k = slotKeyOf(slot);
    return _ops.where((o) => o.slotKey == k).firstOrNull;
  }

  /// Loads the queue of [account]; another account's queue is never visible and null (signed out) empties it.
  void bind(String? account) {
    if (account == _account) return;
    if (account == null) {
      clear();
      return;
    }
    _ops.clear();
    _account = account;
    final raw = prefs?.getString('$_keyPrefix$account');
    if (raw != null) {
      try {
        for (final e in jsonDecode(raw) as List) {
          final op = QueuedOp.fromJson(e);
          if (op != null && _ops.length < maxItems) _ops.add(op);
        }
      } catch (_) {
        _ops.clear();
      }
    }
    notifyListeners();
  }

  /// Sign-out: the queue and its stored copy are removed.
  void clear() {
    final old = _account;
    _account = null;
    _ops.clear();
    if (old != null) prefs?.remove('$_keyPrefix$old');
    notifyListeners();
  }

  void _persist() {
    final a = _account;
    if (a == null || prefs == null) return;
    if (_ops.isEmpty) {
      prefs?.remove('$_keyPrefix$a');
    } else {
      prefs?.setString('$_keyPrefix$a', jsonEncode([for (final o in _ops) o.toJson()]));
    }
  }

  static bool _unreachable(RecordsException e) => e.kind == RecordsErrorKind.network || e.kind == RecordsErrorKind.notConnected;

  /// Tries to send now. Only an unreachable server queues the report; any answer from the server (invalid, forbidden,
  /// conflict) is thrown to the caller, because retrying later cannot change it.
  Future<SubmitResult> submitIntake(RecordsClient api, Json slot, String status) async {
    final queuedAt = _now();
    try {
      // No client time on a live report: a phone clock that runs behind would make the server treat a correction as older than the record it just saved.
      await api.logIntake(slot, status);
      _drop(slotKeyOf(slot)); // a newer online report supersedes an older queued one
      return SubmitResult.synced;
    } on RecordsException catch (e) {
      if (!_unreachable(e)) rethrow;
      _enqueue(slot, status, queuedAt);
      return SubmitResult.queued;
    }
  }

  void _drop(String slotKey) {
    if (_ops.any((o) => o.slotKey == slotKey)) {
      _ops.removeWhere((o) => o.slotKey == slotKey);
      _persist();
      notifyListeners();
    }
  }

  void _enqueue(Json slot, String status, DateTime at) {
    final key = slotKeyOf(slot);
    final replaced = _ops.indexWhere((o) => o.slotKey == key);
    if (replaced < 0 && _ops.length >= maxItems) {
      throw const RecordsException(RecordsErrorKind.invalid, 'offline.queue_full');
    }
    final op = QueuedOp(
      id: _uuid(),
      slotKey: key,
      queuedAt: at,
      payload: {
        'patientMedicationId': slot['patientMedicationId'],
        'scheduleEntryId': slot['scheduleEntryId'],
        'scheduledFor': slot['scheduledFor'],
        'status': status,
      },
    );
    if (replaced >= 0) {
      _ops[replaced] = op; // the person's latest decision for this dose wins locally; the server still arbitrates
    } else {
      _ops.add(op);
    }
    _persist();
    notifyListeners();
  }

  /// Sends the pending operations in the order they were made. Stops at the first unreachable answer (the rest stay pending).
  Future<void> sync(RecordsClient api) async {
    if (_syncing) return; // two overlapping syncs would double-send
    _syncing = true;
    notifyListeners();
    try {
      for (final op in _ops.where((o) => o.state == QueueState.pending).toList()..sort((a, b) => a.queuedAt.compareTo(b.queuedAt))) {
        op.attempts++;
        try {
          await api.logIntake(
            {
              'patientMedicationId': op.payload['patientMedicationId'],
              'scheduleEntryId': op.payload['scheduleEntryId'],
              'scheduledFor': op.payload['scheduledFor'],
            },
            op.status,
            clientRecordedAt: op.queuedAt,
          );
          op.state = QueueState.synced;
          op.code = null;
        } on RecordsException catch (e) {
          if (_unreachable(e)) {
            if (op.attempts >= maxAttempts) {
              op.state = QueueState.failed;
              op.code = 'retries_exhausted';
            }
            _persist();
            break;
          }
          if (e.kind == RecordsErrorKind.conflict) {
            op.state = QueueState.conflict;
            op.code = e.code ?? (e.errors.isEmpty ? null : e.errors.first);
          } else {
            op.state = QueueState.failed;
            op.code = e.code ?? e.kind.name;
          }
        }
        _persist();
      }
    } finally {
      _syncing = false;
      notifyListeners();
    }
  }

  /// Synced operations are only a confirmation; they are removed once the screen has shown them.
  void pruneSynced() {
    final before = _ops.length;
    _ops.removeWhere((o) => o.state == QueueState.synced);
    if (_ops.length != before) {
      _persist();
      notifyListeners();
    }
  }

  /// Failed -> pending again (the person asked to retry); the attempt counter restarts.
  void retry(String id) {
    final op = _ops.where((o) => o.id == id && o.state == QueueState.failed).firstOrNull;
    if (op == null) return;
    op.state = QueueState.pending;
    op.attempts = 0;
    op.code = null;
    _persist();
    notifyListeners();
  }

  /// The person gives up the queued report (for a conflict: keeps what the server already has).
  void discard(String id) {
    _ops.removeWhere((o) => o.id == id);
    _persist();
    notifyListeners();
  }

  static String _uuid() {
    final r = Random.secure();
    String h(int n) => List.generate(n, (_) => r.nextInt(16).toRadixString(16)).join();
    return '${h(8)}-${h(4)}-4${h(3)}-${(8 + r.nextInt(4)).toRadixString(16)}${h(3)}-${h(12)}';
  }
}
