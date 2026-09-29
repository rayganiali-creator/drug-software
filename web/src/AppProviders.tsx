import type { ReactNode } from "react";
import { AuthProvider } from "./auth/AuthContext";
import type { AuthBackend } from "./auth/types";
import { ToastProvider } from "./components/ui";
import { I18nProvider, useI18n, type Locale } from "./i18n/I18nProvider";
import { ServicesProvider } from "./services/ServicesProvider";
import type { Services } from "./services/types";
import { ThemeProvider, type ThemeMode } from "./theme/ThemeProvider";

function Toasts({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  return <ToastProvider dismissLabel={t("common.dismiss")}>{children}</ToastProvider>;
}

export function AppProviders({ children, locale, theme, services, auth, withAuth = true }: { children: ReactNode; locale?: Locale; theme?: ThemeMode; services?: Services; auth?: AuthBackend; withAuth?: boolean }) {
  return (
    <I18nProvider initial={locale}>
      <ThemeProvider initial={theme}>
        {withAuth ? (
          <AuthProvider backend={auth}>
            <ServicesProvider services={services}>
              <Toasts>{children}</Toasts>
            </ServicesProvider>
          </AuthProvider>
        ) : (
          <ServicesProvider services={services}>
            <Toasts>{children}</Toasts>
          </ServicesProvider>
        )}
      </ThemeProvider>
    </I18nProvider>
  );
}
