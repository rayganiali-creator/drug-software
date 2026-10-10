import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

/// Authorised access to the MedSmarter API for the app.
///
/// Tokens live ONLY in memory (never on disk, never logged) and are obtained from the API itself, so the app binary contains
/// no secret and no provider key. In this phase the API offers a development-only login for the fictional demo accounts;
/// a real identity provider replaces `login` later without touching the screens that call `get`.
class ApiSession {
  ApiSession({required this.baseUrl, http.Client? client})
    : _client = client ?? http.Client();

  final String baseUrl;
  final http.Client _client;
  String? _access;
  String? _refresh;
  String? _userId;

  /// Id of the signed-in user (the key of their own patient record). Null when signed out.
  String? get userId => _userId;

  bool get hasToken => _access != null;

  Future<bool>? _pendingLogin;

  /// Completes when a login that is already running has finished (so a screen opened right after sign-in waits for its token
  /// instead of reporting "not connected"). Returns at once when nothing is in flight.
  Future<void> whenReady() async {
    final p = _pendingLogin;
    if (p != null) await p;
  }

  static const _timeout = Duration(seconds: 8);

  static String get _platform => switch (defaultTargetPlatform) {
    TargetPlatform.android => 'android',
    TargetPlatform.iOS => 'ios',
    _ => 'other',
  };

  /// Development login for a fictional demo account. Returns false when the API is unreachable or refuses.
  Future<bool> login(String accountId) {
    final f = _login(accountId);
    _pendingLogin = f;
    return f.whenComplete(() {
      if (identical(_pendingLogin, f)) _pendingLogin = null;
    });
  }

  Future<bool> _login(String accountId) async {
    try {
      final res = await _client
          .post(
            Uri.parse('$baseUrl/auth/login'),
            headers: _json,
            body: jsonEncode({
              'provider': 'mock',
              'credentials': {'accountId': accountId},
              'device': {
                'platform': _platform,
                'appVersion': '0.1.0',
                'deviceName': 'MedSmarter app ($_platform)',
              },
            }),
          )
          .timeout(_timeout);
      return _store(res);
    } catch (_) {
      return false;
    }
  }

  void clear() {
    _access = null;
    _refresh = null;
    _userId = null;
  }

  Future<http.Response> get(String path) => send('GET', path);

  /// Authorised request with one automatic token refresh. `body` is sent as JSON.
  Future<http.Response> send(
    String method,
    String path, [
    Object? body,
    Duration? timeout,
  ]) async {
    var res = await _send(method, path, body, timeout);
    if (res.statusCode == 401 && await _refreshTokens()) {
      res = await _send(method, path, body, timeout);
    }
    return res;
  }

  Future<http.Response> _send(
    String method,
    String path,
    Object? body,
    Duration? timeout,
  ) async {
    final req = http.Request(method, Uri.parse('$baseUrl$path'))
      ..headers.addAll({
        'Accept': 'application/json',
        'X-Client': _platform == 'other' ? 'api' : _platform,
        if (_access != null) 'Authorization': 'Bearer $_access',
        if (body != null) 'Content-Type': 'application/json',
      });
    if (body != null) req.body = jsonEncode(body);
    final limit = timeout ?? _timeout;
    final streamed = await _client.send(req).timeout(limit);
    return http.Response.fromStream(streamed).timeout(limit);
  }

  Future<bool> _refreshTokens() async {
    final token = _refresh;
    if (token == null) return false;
    try {
      final res = await _client
          .post(
            Uri.parse('$baseUrl/auth/refresh'),
            headers: _json,
            body: jsonEncode({'refreshToken': token}),
          )
          .timeout(_timeout);
      final ok = _store(res);
      if (!ok) clear();
      return ok;
    } catch (_) {
      return false;
    }
  }

  bool _store(http.Response res) {
    if (res.statusCode != 200) return false;
    final body = jsonDecode(res.body);
    if (body is! Map<String, dynamic>) return false;
    final tokens = body['tokens'];
    if (tokens is! Map<String, dynamic>) return false;
    _access = tokens['accessToken'] as String?;
    _refresh = tokens['refreshToken'] as String?;
    final user = body['user'];
    if (user is Map<String, dynamic>) _userId = user['id'] as String?;
    return _access != null;
  }

  static const _json = {
    'Content-Type': 'application/json',
    'Accept': 'application/json',
  };
}
