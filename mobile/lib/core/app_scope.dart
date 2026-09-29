import 'package:flutter/material.dart';

import 'app_controller.dart';
import 'formatters.dart';
import 'l10n.dart';
import 'services.dart';

/// Makes [AppController] available below the app root and rebuilds dependents on change.
class AppScope extends InheritedNotifier<AppController> {
  const AppScope({
    super.key,
    required AppController controller,
    required super.child,
  }) : super(notifier: controller);

  static AppController of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppScope>();
    assert(scope != null, 'AppScope missing above this widget');
    return scope!.notifier!;
  }
}

extension AppContext on BuildContext {
  AppController get app => AppScope.of(this);
  AppServices get services => app.services;
  Formatters get fmt => app.fmt;
  String t(String key, [Map<String, Object>? params]) => app.t(key, params);
  String loc(Localized v) => app.loc(v);
}
