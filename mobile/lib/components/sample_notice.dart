import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../core/app_scope.dart';
import '../design/tokens.g.dart';
import 'buttons.dart';
import 'display.dart';
import 'tone.dart';

/// Shown on screens that still run on the sample data built into the app (no server call, nothing saved). It says so in plain words and, when a stored-data
/// screen exists for the same job, offers to open it. The global demo banner says the data is fictional; this says the screen is not connected.
class SampleDataNotice extends StatelessWidget {
  const SampleDataNotice({super.key, this.realRoute, this.realLabelKey});
  final String? realRoute;
  final String? realLabelKey;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: Space.s3),
    child: Semantics(
      container: true,
      child: AlertCard(
        title: context.t('sample.title'),
        body: context.t('sample.body'),
        tone: Tone.warning,
        actions: realRoute == null || realLabelKey == null
            ? null
            : [
                AppButton(
                  label: context.t(realLabelKey!),
                  size: ButtonSize.sm,
                  variant: ButtonVariant.secondary,
                  onPressed: () => context.push(realRoute!),
                ),
              ],
      ),
    ),
  );
}
