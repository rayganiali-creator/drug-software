import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/health_client.dart';
import 'package:medsmarter_mobile/main.dart';

void main() {
  testWidgets('shows dependency statuses', (tester) async {
    await tester.pumpWidget(
      MedSmarterApp(
        loadHealth: () async => const HealthReport(
          status: 'Unhealthy',
          checks: {'postgres': 'Healthy', 'kafka': 'Unhealthy'},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Overall: Unhealthy'), findsOneWidget);
    expect(find.text('kafka: Unhealthy'), findsOneWidget);
  });

  testWidgets('shows retry when API is unreachable', (tester) async {
    await tester.pumpWidget(
      MedSmarterApp(loadHealth: () async => throw Exception('down')),
    );
    await tester.pumpAndSettle();
    expect(find.text('The API is unreachable.'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
  });

  group('HealthClient', () {
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
