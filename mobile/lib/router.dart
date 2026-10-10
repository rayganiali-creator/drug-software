import 'package:go_router/go_router.dart';

import 'auth/auth_controller.dart';
import 'features/assistant/assistant_screen.dart';
import 'features/assistant/grounded_screen.dart';
import 'features/auth/login_screen.dart';
import 'features/checkin/checkin_screen.dart';
import 'features/design_system/design_system_screen.dart';
import 'features/drugs/drug_screens.dart';
import 'features/home/home_screen.dart';
import 'features/medications/medications_screen.dart';
import 'features/profile/profile_screen.dart';
import 'features/records/batches_screen.dart';
import 'features/records/messages_screen.dart';
import 'features/records/records_screen.dart';
import 'features/records/sharing_screen.dart';
import 'features/records/taking_screen.dart';
import 'features/shell/app_shell.dart';

/// Patient navigation (see docs/phase2/02-navigation-and-page-map.md).
/// Five tabs keep their own navigation stacks; detail screens push on top of a tab.
GoRouter buildRouter({
  String initialLocation = '/home',
  required AuthController auth,
}) => GoRouter(
  initialLocation: initialLocation,
  refreshListenable: auth,
  // Deny by default: anonymous -> /login; signed in but not a patient -> /no-access. UX only, the API re-checks.
  redirect: (context, state) {
    final loc = state.matchedLocation;
    if (!auth.isAuthenticated) return loc == '/login' ? null : '/login';
    if (!auth.canUsePatientApp) {
      return loc == '/no-access' ? null : '/no-access';
    }
    if (loc == '/login' || loc == '/no-access') return '/home';
    return null;
  },
  routes: [
    GoRoute(path: '/login', builder: (c, s) => const LoginScreen()),
    GoRoute(path: '/no-access', builder: (c, s) => const NoAccessScreen()),
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
                // Drug reference (real API). Declared before ':id' so "reference" is never read as a drug id.
                GoRoute(
                  path: 'reference',
                  builder: (c, s) => const DrugSearchScreen(),
                  routes: [
                    GoRoute(
                      path: ':id',
                      builder: (c, s) =>
                          DrugDetailScreen(id: s.pathParameters['id']!),
                    ),
                  ],
                ),
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
                GoRoute(
                  path: 'live',
                  builder: (c, s) => GroundedAssistantScreen(
                    initialQuestion: s.uri.queryParameters['q'],
                  ),
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
                GoRoute(path: 'records', builder: (c, s) => const RecordsScreen()),
                GoRoute(path: 'taking', builder: (c, s) => const TakingScreen()),
                GoRoute(path: 'batches', builder: (c, s) => const BatchesScreen()),
                GoRoute(path: 'sharing', builder: (c, s) => const SharingScreen()),
                GoRoute(path: 'messages', builder: (c, s) => const MessagesScreen()),
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
