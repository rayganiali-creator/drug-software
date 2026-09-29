import 'package:go_router/go_router.dart';

import 'features/assistant/assistant_screen.dart';
import 'features/checkin/checkin_screen.dart';
import 'features/design_system/design_system_screen.dart';
import 'features/home/home_screen.dart';
import 'features/medications/medications_screen.dart';
import 'features/profile/profile_screen.dart';
import 'features/shell/app_shell.dart';

/// Patient navigation (see docs/phase2/02-navigation-and-page-map.md).
/// Five tabs keep their own navigation stacks; detail screens push on top of a tab.
GoRouter buildRouter({String initialLocation = '/home'}) => GoRouter(
  initialLocation: initialLocation,
  routes: [
    StatefulShellRoute.indexedStack(
      builder: (context, state, shell) => AppShell(shell: shell),
      branches: [
        StatefulShellBranch(
          routes: [
            GoRoute(path: '/home', builder: (c, s) => const HomeScreen()),
          ],
        ),
        StatefulShellBranch(
          routes: [
            GoRoute(
              path: '/medications',
              builder: (c, s) => const MedicationsScreen(),
              routes: [
                GoRoute(
                  path: ':id',
                  builder: (c, s) =>
                      MedicationDetailScreen(drugId: s.pathParameters['id']!),
                ),
              ],
            ),
          ],
        ),
        StatefulShellBranch(
          routes: [
            GoRoute(
              path: '/assistant',
              builder: (c, s) => AssistantScreen(
                initialQuestion: s.uri.queryParameters['q'],
                drugId: s.uri.queryParameters['drug'],
              ),
              routes: [
                GoRoute(
                  path: 'history',
                  builder: (c, s) => const ConversationHistoryScreen(),
                ),
              ],
            ),
          ],
        ),
        StatefulShellBranch(
          routes: [
            GoRoute(path: '/checkin', builder: (c, s) => const CheckInScreen()),
          ],
        ),
        StatefulShellBranch(
          routes: [
            GoRoute(
              path: '/profile',
              builder: (c, s) => const ProfileScreen(),
              routes: [
                GoRoute(
                  path: 'design-system',
                  builder: (c, s) => const DesignSystemScreen(),
                ),
              ],
            ),
          ],
        ),
      ],
    ),
  ],
);
