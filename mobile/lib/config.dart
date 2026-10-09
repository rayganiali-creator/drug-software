import 'package:flutter/foundation.dart';

/// Build-time configuration, e.g. `--dart-define=API_BASE_URL=http://10.0.2.2:5080`.
/// Nothing secret belongs here: everything in the app binary is readable by users.
/// The address of the backend is configuration; credentials and provider keys never are (they stay on the server).
class AppConfig {
  const AppConfig._();

  static const String _configured = String.fromEnvironment('API_BASE_URL');

  /// Explicit `API_BASE_URL` wins. Otherwise: the Android emulator's alias for the host machine, or localhost on
  /// iOS simulator, desktop and web (all of which reach the developer machine directly).
  static String get apiBaseUrl {
    if (_configured.isNotEmpty) return _configured;
    return !kIsWeb && defaultTargetPlatform == TargetPlatform.android
        ? 'http://10.0.2.2:5080'
        : 'http://localhost:5080';
  }

  static const String _autoSignIn = String.fromEnvironment(
    'DEBUG_AUTO_SIGN_IN',
  );
  static const String _initialRoute = String.fromEnvironment(
    'DEBUG_INITIAL_ROUTE',
  );

  /// DEVELOPER AID, debug builds only (ignored in profile/release): sign in as this fictional demo account at start-up.
  static String? get debugAutoSignIn =>
      kDebugMode && _autoSignIn.isNotEmpty ? _autoSignIn : null;

  /// DEVELOPER AID, debug builds only: open this route first (e.g. for screenshots on a machine without input devices).
  static String? get debugInitialRoute =>
      kDebugMode && _initialRoute.isNotEmpty ? _initialRoute : null;
}
