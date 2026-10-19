import { StatCard } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { useOpenAdrCount } from "./hooks";

/** Dashboard tile with the real open-queue count. Loading, refusal and "not connected" are shown as such; a sample number is never substituted. */
export function OpenAdrTile({ labelKey }: { labelKey: string }) {
  const { t, fmt } = useI18n();
  const count = useOpenAdrCount();
  // A number is shown only when the server stated one (X-Total-Count: the open records of the patients this reviewer may read, counted by the server). Never the length of a list.
  if (count.status === "ready" && count.data.count !== null) return <StatCard icon="alertTriangle" label={t(labelKey)} value={fmt.number(count.data.count)} hint={t("adr.tile.scope")} />;
  if (count.status === "ready") return <StatCard icon="alertTriangle" label={t(labelKey)} value="—" hint={t("adr.tile.incomplete")} />;
  if (count.status === "loading") return <StatCard icon="alertTriangle" label={t(labelKey)} value="…" />;
  return <StatCard icon="alertTriangle" label={t(labelKey)} value="—" hint={t("adr.tile.unavailable")} />;
}
