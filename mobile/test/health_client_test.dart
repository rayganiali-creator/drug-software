import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/api/health_client.dart';

void main() {
  group('HealthClient (Phase 1 API probe)', () {
    test('returns the report on HTTP 503', () async {
      final client = HealthClient(
        baseUrl: 'http://x',
        client: MockClient((req) async {
          expect(req.url.toString(), 'http://x/health/ready');
          return http.Response(
            '{"status":"Unhealthy","checks":{"redis":"Unhealthy"}}',
            503,
          );
        }),
      );
      final r = await client.fetchReadiness();
      expect(r.isHealthy, isFalse);
      expect(r.checks['redis'], 'Unhealthy');
    });

    test('rejects malformed bodies', () async {
      final client = HealthClient(
        baseUrl: 'http://x',
        client: MockClient((_) async => http.Response('{"nope":1}', 200)),
      );
      expect(client.fetchReadiness(), throwsFormatException);
    });
  });
}
