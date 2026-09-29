import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:go_router/go_router.dart';

import 'core/app_controller.dart';
import 'core/app_scope.dart';
import 'design/theme.dart';
import 'router.dart';

class MedSmarterApp extends StatefulWidget {
  const MedSmarterApp({
    super.key,
    required this.controller,
    this.initialLocation = '/home',
  });
  final AppController controller;
  final String initialLocation;
  @override
  State<MedSmarterApp> createState() => _MedSmarterAppState();
}

class _MedSmarterAppState extends State<MedSmarterApp> {
  late final GoRouter _router = buildRouter(
    initialLocation: widget.initialLocation,
  );

  @override
  void dispose() {
    _router.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AppScope(
      controller: widget.controller,
      child: ListenableBuilder(
        listenable: widget.controller,
        builder: (context, _) => MaterialApp.router(
          debugShowCheckedModeBanner: false,
          title: widget.controller.t('app.name'),
          theme: buildTheme(Brightness.light),
          darkTheme: buildTheme(Brightness.dark),
          themeMode: widget.controller.themeMode,
          locale: widget.controller.locale,
          supportedLocales: const [Locale('fa'), Locale('en')],
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          routerConfig: _router,
        ),
      ),
    );
  }
}
