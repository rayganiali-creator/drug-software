import 'package:flutter/material.dart';

import 'config.dart';
import 'health_client.dart';

void main() {
  runApp(
    MedSmarterApp(
      loadHealth: HealthClient(baseUrl: AppConfig.apiBaseUrl).fetchReadiness,
    ),
  );
}

class MedSmarterApp extends StatelessWidget {
  const MedSmarterApp({super.key, required this.loadHealth});

  final Future<HealthReport> Function() loadHealth;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'AI MedSmarter',
      theme: ThemeData(colorSchemeSeed: Colors.teal, useMaterial3: true),
      home: StatusPage(loadHealth: loadHealth),
    );
  }
}

class StatusPage extends StatefulWidget {
  const StatusPage({super.key, required this.loadHealth});

  final Future<HealthReport> Function() loadHealth;

  @override
  State<StatusPage> createState() => _StatusPageState();
}

class _StatusPageState extends State<StatusPage> {
  late Future<HealthReport> _future = widget.loadHealth();

  void _retry() => setState(() => _future = widget.loadHealth());

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('AI MedSmarter')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: FutureBuilder<HealthReport>(
          future: _future,
          builder: (context, snap) {
            if (snap.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snap.hasError) {
              return Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Text('The API is unreachable.'),
                  const SizedBox(height: 8),
                  FilledButton(onPressed: _retry, child: const Text('Retry')),
                ],
              );
            }
            final report = snap.requireData;
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Foundation build. No clinical features yet.'),
                const SizedBox(height: 16),
                Text('Overall: ${report.status}'),
                for (final e in report.checks.entries)
                  Text('${e.key}: ${e.value}'),
              ],
            );
          },
        ),
      ),
    );
  }
}
