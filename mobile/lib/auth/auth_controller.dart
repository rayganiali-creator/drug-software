import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'access.g.dart';

enum AuthStatus { anonymous, authenticated }

enum SignedOutReason { none, signedOut, expired, disabled }

enum SignInError { invalid, disabled, noRole, throttled }

enum ConsentStatus { active, expired, revoked }

class AuthUser {
  const AuthUser({
    required this.id,
    required this.displayName,
    required this.roles,
    required this.permissions,
    this.subjectKey,
  });
  final String id;
  final Map<String, String> displayName;
  final List<String> roles;
  final Set<String> permissions;
  final String? subjectKey;
}

class DemoAccount {
  const DemoAccount({
    required this.id,
    required this.displayName,
    required this.description,
    required this.roles,
    required this.primary,
    required this.disabled,
  });
  final String id;
  final Map<String, String> displayName;
  final Map<String, String> description;
  final List<String> roles;
  final bool primary;
  final bool disabled;
}

class ConsentItem {
  ConsentItem({
    required this.id,
    required this.grantee,
    required this.purpose,
    required this.scope,
    required this.expiresAt,
    this.revokedAt,
  });
  final String id;
  final Map<String, String> grantee;
  final String purpose;
  final List<String> scope;
  final DateTime expiresAt;
  DateTime? revokedAt;
}

const _sessionKey = 'auth.demo-session';
const _sessionHours = 8;

/// DEMO ONLY sign-in state. Fictional accounts come from the shared access catalog (the same file the API embeds);
/// there are no passwords and nothing leaves the device. It mirrors the server's role/permission mapping so the UI can
/// be explored without a backend, but the server remains the security boundary. A real identity provider replaces
/// this class later without touching the screens.
class AuthController extends ChangeNotifier {
  AuthController._(this._catalog, this._prefs, this._now);

  final Map<String, dynamic> _catalog;
  final SharedPreferences? _prefs;
  final DateTime Function() _now;
  String? _accountId;
  DateTime? _expiresAt;
  SignedOutReason _reason = SignedOutReason.none;
  final Map<String, DateTime?> _revoked = {};

  static Future<AuthController> create({
    AssetBundle? bundle,
    SharedPreferences? prefs,
    DateTime Function()? clock,
    String? initialAccountId,
  }) async {
    final raw = await (bundle ?? rootBundle).loadString(
      'assets/shared/access-catalog.json',
    );
    final c = AuthController._(
      jsonDecode(raw) as Map<String, dynamic>,
      prefs,
      clock ?? DateTime.now,
    );
    if (initialAccountId != null) {
      c._accountId = initialAccountId;
      c._expiresAt = c._now().add(const Duration(hours: _sessionHours));
    } else {
      c._restore();
    }
    return c;
  }

  List<Map<String, dynamic>> get _accounts =>
      (_catalog['demoAccounts'] as List).cast<Map<String, dynamic>>();

  Map<String, dynamic>? _account(String id) =>
      _accounts.where((a) => a['id'] == id).firstOrNull;

  void _restore() {
    try {
      final raw = _prefs?.getString(_sessionKey);
      if (raw == null) return;
      final m = jsonDecode(raw) as Map<String, dynamic>;
      _accountId = m['accountId'] as String?;
      _expiresAt = DateTime.tryParse(m['expiresAt'] as String? ?? '');
    } catch (_) {
      _accountId = null;
    }
  }

  void _persist() {
    if (_accountId == null) {
      _prefs?.remove(_sessionKey);
    } else {
      _prefs?.setString(
        _sessionKey,
        jsonEncode({
          'accountId': _accountId,
          'expiresAt': _expiresAt!.toIso8601String(),
        }),
      );
    }
  }

  AuthUser? get user {
    final id = _accountId;
    if (id == null) return null;
    if (_expiresAt == null || !_now().isBefore(_expiresAt!)) return null;
    final a = _account(id);
    if (a == null || a['status'] == 'Disabled') return null;
    final roles = {
      for (final r in (a['roles'] as List)) (r as Map)['role'] as String,
    }.toList();
    if (roles.isEmpty) return null;
    final perms = <String>{};
    for (final r in (_catalog['roles'] as List).cast<Map<String, dynamic>>()) {
      if (roles.contains(r['name'])) {
        perms.addAll((r['permissions'] as List).cast<String>());
      }
    }
    final subject = (_catalog['subjects'] as List)
        .cast<Map<String, dynamic>>()
        .where((s) => s['accountId'] == id)
        .firstOrNull;
    return AuthUser(
      id: a['userId'] as String,
      displayName: (a['displayName'] as Map).cast<String, String>(),
      roles: roles,
      permissions: perms,
      subjectKey: subject?['key'] as String?,
    );
  }

  AuthStatus get status =>
      user == null ? AuthStatus.anonymous : AuthStatus.authenticated;
  bool get isAuthenticated => status == AuthStatus.authenticated;
  SignedOutReason get reason => _reason;

  bool can(String permission) =>
      user?.permissions.contains(permission) ?? false;

  /// The mobile app is the patient app: only accounts that can submit check-ins (patients) may use it.
  bool get canUsePatientApp => can(Permissions.patientCheckinsCreate);

  List<DemoAccount> get demoAccounts => [
    for (final a in _accounts.where((a) => a['loginEnabled'] == true))
      DemoAccount(
        id: a['id'] as String,
        displayName: (a['displayName'] as Map).cast<String, String>(),
        description: (a['description'] as Map).cast<String, String>(),
        roles: {
          for (final r in (a['roles'] as List)) (r as Map)['role'] as String,
        }.toList(),
        primary: a['primary'] == true,
        disabled: a['status'] == 'Disabled',
      ),
  ];

  SignInError? signIn(String accountId) {
    final a = _account(accountId);
    if (a == null || a['loginEnabled'] != true) return SignInError.invalid;
    if (a['status'] == 'Disabled') {
      _reason = SignedOutReason.disabled;
      notifyListeners();
      return SignInError.disabled;
    }
    _accountId = accountId;
    _expiresAt = _now().add(const Duration(hours: _sessionHours));
    _reason = SignedOutReason.none;
    _persist();
    notifyListeners();
    return null;
  }

  void signOut() {
    _accountId = null;
    _expiresAt = null;
    _reason = SignedOutReason.signedOut;
    _persist();
    notifyListeners();
  }

  /// DEMO DEV: end the session as if it had expired.
  void simulateExpiry() {
    _accountId = null;
    _expiresAt = null;
    _reason = SignedOutReason.expired;
    _persist();
    notifyListeners();
  }

  // ---- consents (the signed-in patient's own) ----
  List<ConsentItem> _consentCache = [];
  String? _consentOwner;

  List<ConsentItem> get consents {
    final id = _accountId;
    if (id == null) return const [];
    if (_consentOwner != id) {
      final now = _now();
      _consentOwner = id;
      final list = (_catalog['consents'] as List).cast<Map<String, dynamic>>();
      _consentCache = [
        for (var i = 0; i < list.length; i++)
          if (list[i]['subjectAccountId'] == id)
            ConsentItem(
              id: 'consent-$i',
              grantee: _granteeName(list[i]),
              purpose: list[i]['purpose'] as String,
              scope: (list[i]['scope'] as List).cast<String>(),
              expiresAt: now.add(
                Duration(days: list[i]['expiresInDays'] as int),
              ),
              revokedAt: _revoked['consent-$i'],
            ),
      ];
    }
    return _consentCache;
  }

  Map<String, String> _granteeName(Map<String, dynamic> c) {
    final acct = c['granteeAccountId'] as String?;
    if (acct != null) {
      return (_account(acct)?['displayName'] as Map?)?.cast<String, String>() ??
          {'en': '?', 'fa': '?'};
    }
    final key = c['granteeOrganizationKey'] as String?;
    final org = (_catalog['organizations'] as List)
        .cast<Map<String, dynamic>>()
        .where((o) => o['key'] == key)
        .firstOrNull;
    final name = org?['name'] as String? ?? '?';
    return {'en': name, 'fa': name};
  }

  ConsentStatus consentStatus(ConsentItem c) => c.revokedAt != null
      ? ConsentStatus.revoked
      : !_now().isBefore(c.expiresAt)
      ? ConsentStatus.expired
      : ConsentStatus.active;

  /// Only the signed-in subject sees (and therefore can revoke) their own consents. Effective immediately.
  void revokeConsent(String id) {
    final item = consents.where((c) => c.id == id).firstOrNull;
    if (item == null) return;
    item.revokedAt ??= _now();
    _revoked[id] = item.revokedAt;
    notifyListeners();
  }
}
