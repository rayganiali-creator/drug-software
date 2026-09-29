import 'dart:convert';

import 'package:http/http.dart' as http;

class HealthReport {
  const HealthReport({required this.status, required this.checks});

  final String status;
  final Map<String, String> checks;

  bool get isHealthy => status == 'Healthy';

  static HealthReport? tryParse(Object? json) {
    if (json is! Map<String, dynamic>) return null;
    final status = json['status'];
    final checks = json['checks'];
    if (status is! String || checks is! Map<String, dynamic>) return null;
    return HealthReport(
      status: status,
      checks: checks.map((k, v) => MapEntry(k, v.toString())),
    );
  }
}

class HealthClient {
  HealthClient({required this.baseUrl, http.Client? client})
    : _client = client ?? http.Client();

  final String baseUrl;
  final http.Client _client;

  /// Reads `/health/ready`. HTTP 503 still carries a valid report, so it is returned, not thrown.
  Future<HealthReport> fetchReadiness() async {
    final res = await _client
        .get(
          Uri.parse('$baseUrl/health/ready'),
          headers: {'Accept': 'application/json'},
        )
        .timeout(const Duration(seconds: 5));
    final report = HealthReport.tryParse(jsonDecode(res.body));
    if (report == null) {
      throw const FormatException('Unexpected health response');
    }
    return report;
  }
}
