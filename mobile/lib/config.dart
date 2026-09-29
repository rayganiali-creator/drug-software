/// Build-time configuration, e.g. `--dart-define=API_BASE_URL=http://10.0.2.2:5080`.
/// Nothing secret belongs here: everything in the app binary is readable by users.
class AppConfig {
  const AppConfig._();

  /// Default targets the Android emulator's alias for the host machine.
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5080',
  );
}
