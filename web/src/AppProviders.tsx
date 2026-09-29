import type { ReactNode } from "react";
import { ToastProvider } from "./components/ui";
import { I18nProvider, useI18n, type Locale } from "./i18n/I18nProvider";
import { ServicesProvider } from "./services/ServicesProvider";
import type { Services } from "./services/types";
import { ThemeProvider, type ThemeMode } from "./theme/ThemeProvider";

function Toasts({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  return <ToastProvider dismissLabel={t("common.dismiss")}>{children}</ToastProvider>;
}

export function AppProviders({ children, locale, theme, services }: { children: ReactNode; locale?: Locale; theme?: ThemeMode; services?: Services }) {
  return (
    <I18nProvider initial={locale}>
      <ThemeProvider initial={theme}>
        <ServicesProvider services={services}>
          <Toasts>{children}</Toasts>
        </ServicesProvider>
      </ThemeProvider>
    </I18nProvider>
  );
}
