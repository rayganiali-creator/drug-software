import { describe, expect, it } from "vitest";
import { createMockServices } from "../services/mock/createMockServices";

const at = (h: number, m = 0) => () => new Date(2026, 8, 29, h, m);
const svc = (h = 10, m = 0) => createMockServices({ latencyMs: 0, now: at(h, m) });

describe("mock services: doses", () => {
  it("picks the next upcoming dose and marks past ones", async () => {
    const doses = await svc(10, 0).medications.todaysDoses("pt-sara");
    const next = doses.find((d) => d.isNext)!;
    expect(next.time).toBe("13:00");
    expect(doses.filter((d) => d.time < "10:00").every((d) => d.status === "taken" || d.status === "missed")).toBe(true);
    expect(doses.some((d) => d.status === "missed")).toBe(true); // seeded missed dose
  });

  it("rolls the next dose over to tomorrow after the last dose", async () => {
    const doses = await svc(23, 30).medications.todaysDoses("pt-sara");
    const next = doses.find((d) => d.isNext)!;
    expect(next.tomorrow).toBe(true);
    expect(next.time).toBe("08:00");
  });

  it("records a dose as taken and can undo it", async () => {
    const s = svc(7, 0);
    const before = (await s.medications.todaysDoses("pt-sara")).find((d) => d.isNext)!;
    const after = await s.medications.setDose(before.id, "taken");
    expect(after.find((d) => d.id === before.id)!.status).toBe("taken");
    const undone = await s.medications.setDose(before.id, "upcoming");
    expect(undone.find((d) => d.id === before.id)!.status).toBe("upcoming");
  });
});

describe("mock services: data is clearly demo", () => {
  it("marks every drug, patient and prescription as demo data", async () => {
    const s = svc();
    const patients = await s.patients.list();
    const rx = await s.prescriptions.list();
    expect(patients.every((p) => p.demo === true)).toBe(true);
    expect(rx.every((r) => r.demo === true && r.items.every((i) => i.drug.demo))).toBe(true);
  });

  it("uses only fictional ingredient names", async () => {
    const meds = await svc().medications.forPatient("pt-sara");
    expect(meds.every((m) => m.drug.activeIngredient.name.en.includes("(fictional)"))).toBe(true);
  });
});

describe("mock services: interactions and prescriptions", () => {
  it("flags the fictional Demopril + Nocturin interaction", async () => {
    const ix = await svc().prescriptions.interactionsFor("rx-1001");
    expect(ix.map((x) => x.id)).toContain("ix-1");
    expect(ix[0]!.source.demo).toBe(true);
  });
  it("filters prescriptions by patient and status", async () => {
    const s = svc();
    expect((await s.prescriptions.list({ patientId: "pt-sara" })).every((r) => r.patient.id === "pt-sara")).toBe(true);
    expect((await s.prescriptions.list({ status: "pending-review" })).length).toBeGreaterThan(0);
  });
});

describe("mock AI service (no LLM)", () => {
  const ask = (text: string, contextDrugId?: string) => svc().ai.ask({ text, locale: "en", contextDrugId });

  it("answers from a demo source with citations and confidence", async () => {
    const a = await ask("What should I do if I miss a dose?");
    expect(a.kind).toBe("answer");
    expect(a.sources.length).toBeGreaterThan(0);
    expect(a.evidence.length).toBeGreaterThan(0);
    expect(["high", "medium", "low"]).toContain(a.confidence);
  });

  it("answers in Persian queries too", async () => {
    const a = await svc().ai.ask({ text: "اگر دوز را فراموش کنم چه کنم؟", locale: "fa" });
    expect(a.kind).toBe("answer");
  });

  it("refuses instead of guessing when no source matches", async () => {
    const a = await ask("Explain quantum tunnelling");
    expect(a.kind).toBe("refusal");
    expect(a.sources).toEqual([]);
    expect(a.canEscalate).toBe(true);
    expect(a.confidence).toBe("low");
  });

  it("returns a static red-flag response for emergencies (English and Persian)", async () => {
    const en = await ask("I have chest pain");
    expect(en.kind).toBe("red-flag");
    expect(en.sources).toEqual([]);
    expect((await svc().ai.ask({ text: "درد قفسه سینه دارم", locale: "fa" })).kind).toBe("red-flag");
  });

  it("never gives dose-change advice in any canned answer", async () => {
    for (const q of ["missed dose", "with food", "together", "warnings"]) {
      const a = await ask(q);
      expect(a.text.en.toLowerCase()).not.toMatch(/increase (the )?dose|take (an )?extra|stop taking/);
    }
  });

  it("uses drug context when the question is short", async () => {
    const a = await ask("What are the warnings?", "drug-nocturin");
    expect(a.kind).toBe("answer");
    expect(a.text.en).toContain("Nocturin");
  });
});

describe("industry analytics", () => {
  it("exposes a k threshold and aggregate-only data", async () => {
    const s = svc();
    expect(s.analytics.k()).toBeGreaterThan(1);
    const trend = await s.analytics.adrTrend();
    expect(trend.some((t) => t.counts.some((c) => c < s.analytics.k()))).toBe(true); // proves suppression is exercised
    expect(JSON.stringify(trend)).not.toMatch(/pt-|patientId/);
  });
});

describe("pharmacy workflow", () => {
  it("advances a dispensing item one stage at a time and stops at handed", async () => {
    const s = svc();
    const stageAfter = async () => (await s.pharmacy.advance("dp-1")).find((d) => d.id === "dp-1")!.stage;
    expect(await stageAfter()).toBe("preparing");
    expect(await stageAfter()).toBe("ready");
    expect(await stageAfter()).toBe("handed");
    expect(await stageAfter()).toBe("handed");
  });
});
