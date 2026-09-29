import type { ReactNode } from "react";
import { useI18n } from "../../i18n/I18nProvider";
import { Card } from "./display";

export interface Series { id: string; label: string; values: number[]; color: string }

const W = 600;
const H = 220;
const PAD = { t: 12, r: 12, b: 28, l: 34 };

function useScale(values: number[][], rtl: boolean, count: number, yMaxOverride?: number) {
  const max = yMaxOverride ?? Math.max(1, ...values.flat());
  const niceMax = Math.ceil(max / 5) * 5 || 5;
  const iw = W - PAD.l - PAD.r;
  const ih = H - PAD.t - PAD.b;
  const x = (i: number) => {
    const t = count <= 1 ? 0.5 : i / (count - 1);
    return PAD.l + (rtl ? 1 - t : t) * iw;
  };
  const y = (v: number) => PAD.t + ih - (v / niceMax) * ih;
  return { x, y, niceMax, ih, iw };
}

function DataTable({ series, xLabels, caption, format }: { series: Series[]; xLabels: string[]; caption: string; format(n: number): string }) {
  return (
    <div className="sr-only"><table>
      <caption>{caption}</caption>
      <thead><tr><th scope="col" />{series.map((s) => <th key={s.id} scope="col">{s.label}</th>)}</tr></thead>
      <tbody>{xLabels.map((l, i) => <tr key={l + i}><th scope="row">{l}</th>{series.map((s) => <td key={s.id}>{format(s.values[i] ?? 0)}</td>)}</tr>)}</tbody>
    </table></div>
  );
}

export interface ChartProps {
  series: Series[];
  xLabels: string[];
  /** Screen-reader summary of the chart. */
  summary: string;
  yMax?: number;
  /** Values below this are drawn as suppressed (industry k-anonymity). */
  suppressBelow?: number;
}

export function LineChart({ series, xLabels, summary, yMax }: ChartProps) {
  const { dir, fmt } = useI18n();
  const rtl = dir === "rtl";
  const { x, y, niceMax } = useScale(series.map((s) => s.values), rtl, xLabels.length, yMax);
  const ticks = [0, 0.5, 1].map((t) => Math.round(niceMax * t));
  const step = Math.max(1, Math.ceil(xLabels.length / 6));
  return (
    <div>
      <svg className="ms-chart" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={summary} style={{ inlineSize: "100%", blockSize: "auto" }}>
        {ticks.map((tv) => (
          <g key={tv}>
            <line className="ms-chart__grid" x1={PAD.l} x2={W - PAD.r} y1={y(tv)} y2={y(tv)} />
            <text x={rtl ? W - PAD.l + 6 : PAD.l - 6} y={y(tv) + 4} textAnchor={rtl ? "start" : "end"}>{fmt.number(tv)}</text>
          </g>
        ))}
        {xLabels.map((l, i) => (i % step === 0 ? <text key={l + i} x={x(i)} y={H - 8} textAnchor="middle">{l}</text> : null))}
        {series.map((s) => (
          <g key={s.id}>
            <polyline fill="none" stroke={s.color} strokeWidth={2.5} strokeLinejoin="round" strokeLinecap="round" points={s.values.map((v, i) => `${x(i)},${y(v)}`).join(" ")} />
            {s.values.length <= 14 && s.values.map((v, i) => <circle key={i} cx={x(i)} cy={y(v)} r={3} fill="var(--color-surface)" stroke={s.color} strokeWidth={2} />)}
          </g>
        ))}
      </svg>
      <DataTable series={series} xLabels={xLabels} caption={summary} format={(n) => fmt.number(n)} />
    </div>
  );
}

export function BarChart({ series, xLabels, summary, yMax, suppressBelow }: ChartProps) {
  const { dir, fmt } = useI18n();
  const rtl = dir === "rtl";
  const { x, y, niceMax } = useScale(series.map((s) => s.values), rtl, xLabels.length, yMax);
  const groupW = (W - PAD.l - PAD.r) / xLabels.length;
  const barW = Math.min(28, (groupW * 0.7) / series.length);
  const ticks = [0, 0.5, 1].map((t) => Math.round(niceMax * t));
  const step = Math.max(1, Math.ceil(xLabels.length / 8));
  return (
    <div>
      <svg className="ms-chart" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={summary} style={{ inlineSize: "100%", blockSize: "auto" }}>
        {ticks.map((tv) => (
          <g key={tv}>
            <line className="ms-chart__grid" x1={PAD.l} x2={W - PAD.r} y1={y(tv)} y2={y(tv)} />
            <text x={rtl ? W - PAD.l + 6 : PAD.l - 6} y={y(tv) + 4} textAnchor={rtl ? "start" : "end"}>{fmt.number(tv)}</text>
          </g>
        ))}
        {xLabels.map((l, i) => {
          const cx = x(i) + (rtl ? -1 : 1) * 0;
          return (
            <g key={l + i}>
              {series.map((s, si) => {
                const v = s.values[i] ?? 0;
                const suppressed = suppressBelow !== undefined && v < suppressBelow;
                const bx = cx - (barW * series.length) / 2 + si * barW;
                const h = y(0) - y(suppressed ? suppressBelow : v);
                return <rect key={s.id} x={bx} y={y(0) - h} width={barW - 2} height={h} rx={4} fill={suppressed ? "none" : s.color} stroke={s.color} strokeWidth={suppressed ? 1.5 : 0} strokeDasharray={suppressed ? "3 3" : undefined} />;
              })}
              {i % step === 0 && <text x={cx} y={H - 8} textAnchor="middle">{l}</text>}
            </g>
          );
        })}
      </svg>
      <DataTable series={series} xLabels={xLabels} caption={summary} format={(n) => (suppressBelow !== undefined && n < suppressBelow ? `<${fmt.number(suppressBelow)}` : fmt.number(n))} />
    </div>
  );
}

export function Sparkline({ values, label, color = "var(--color-chart1)", width = 96, height = 28 }: { values: number[]; label: string; color?: string; width?: number; height?: number }) {
  const { dir } = useI18n();
  const rtl = dir === "rtl";
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = Math.max(1, max - min);
  const pts = values.map((v, i) => {
    const t = values.length <= 1 ? 0.5 : i / (values.length - 1);
    return `${(rtl ? 1 - t : t) * (width - 4) + 2},${height - 3 - ((v - min) / span) * (height - 6)}`;
  });
  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label}>
      <polyline fill="none" stroke={color} strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" points={pts.join(" ")} />
    </svg>
  );
}

export function Legend({ series }: { series: Pick<Series, "id" | "label" | "color">[] }) {
  return (
    <div className="ms-legend">
      {series.map((s) => (
        <span key={s.id} className="ms-legend__item"><span className="ms-legend__swatch" style={{ background: s.color }} />{s.label}</span>
      ))}
    </div>
  );
}

export function ChartCard({ title, subtitle, legend, actions, children, footer }: { title: string; subtitle?: string; legend?: Series[]; actions?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <Card title={title} subtitle={subtitle} actions={actions}>
      <div className="ms-stack" style={{ gap: "var(--space-3)" }}>
        {legend && <Legend series={legend} />}
        {children}
        {footer && <div className="ms-card__subtitle">{footer}</div>}
      </div>
    </Card>
  );
}
