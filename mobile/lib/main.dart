import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app.dart';
import 'core/app_controller.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  SharedPreferences? prefs;
  try {
    prefs = await SharedPreferences.getInstance();
  } catch (_) {
    // Preferences are a convenience; the app works without them.
  }
  final controller = await AppController.create(prefs: prefs);
  runApp(MedSmarterApp(controller: controller));
}
