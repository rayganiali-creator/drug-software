import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../AppProviders";
import { MedicationCard, PatientCard } from "../components/health/cards";
import { AlertCard, Button, Checkbox, Chip, IconButton, Modal, ProgressRing, SegmentedControl, Switch, Table, Tabs, TextField, useToast } from "../components/ui";
import { createMockServices } from "../services/mock/createMockServices";

afterEach(cleanup);
const wrap = (ui: React.ReactNode, locale: "fa" | "en" = "en") => render(<MemoryRouter><AppProviders locale={locale} theme="light" services={createMockServices({ latencyMs: 0 })}>{ui}</AppProviders></MemoryRouter>);

describe("Button / IconButton", () => {
  it("is disabled and busy while loading", () => {
    wrap(<Button loading>Save</Button>);
    const b = screen.getByRole("button", { name: /save/i });
    expect(b).toBeDisabled();
    expect(b).toHaveAttribute("aria-busy", "true");
  });
  it("icon-only buttons expose an accessible name", () => {
    wrap(<IconButton icon="plus" label="Add item" />);
    expect(screen.getByRole("button", { name: "Add item" })).toBeInTheDocument();
  });
});

describe("form controls", () => {
  it("TextField links label, hint and error", () => {
    wrap(<TextField label="Name" error="Required" />);
    const input = screen.getByLabelText("Name");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("Required");
    expect(input.getAttribute("aria-describedby")).toBe(screen.getByRole("alert").id);
  });
  it("Switch and Checkbox toggle via keyboard/click", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    function S() { const [v, setV] = useState(false); return <Switch label="Reminders" checked={v} onCheckedChange={(x) => { setV(x); onChange(x); }} />; }
    wrap(<><S /><Checkbox label="Agree" /></>);
    await user.click(screen.getByRole("switch", { name: "Reminders" }));
    expect(onChange).toHaveBeenCalledWith(true);
    await user.click(screen.getByRole("checkbox", { name: "Agree" }));
    expect(screen.getByRole("checkbox", { name: "Agree" })).toBeChecked();
  });
  it("SegmentedControl is a radiogroup", async () => {
    const user = userEvent.setup();
    const on = vi.fn();
    wrap(<SegmentedControl label="View" value="a" onValueChange={on} options={[{ value: "a", label: "A" }, { value: "b", label: "B" }]} />);
    await user.click(screen.getByLabelText("B"));
    expect(on).toHaveBeenCalledWith("b");
    expect(screen.getByRole("radiogroup", { name: "View" })).toBeInTheDocument();
  });
  it("Chip works as a toggle", async () => {
    const user = userEvent.setup();
    const on = vi.fn();
    wrap(<Chip role="checkbox" selected={false} onClick={on}>Headache</Chip>);
    await user.click(screen.getByRole("checkbox", { name: "Headache" }));
    expect(on).toHaveBeenCalled();
  });
});

describe("Tabs", () => {
  function T() { const [v, setV] = useState("a"); return <Tabs label="Sections" value={v} onValueChange={setV} tabs={[{ id: "a", label: "One", panel: <p>Panel A</p> }, { id: "b", label: "Two", panel: <p>Panel B</p> }, { id: "c", label: "Three", panel: <p>Panel C</p> }]} />; }
  it("supports roving tabindex and arrow keys (LTR)", async () => {
    const user = userEvent.setup();
    wrap(<T />);
    const one = screen.getByRole("tab", { name: "One" });
    expect(one).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("tab", { name: "Two" })).toHaveAttribute("tabindex", "-1");
    one.focus();
    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("tab", { name: "Two" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tabpanel")).toHaveTextContent("Panel B");
    await user.keyboard("{End}");
    expect(screen.getByRole("tab", { name: "Three" })).toHaveFocus();
  });
  it("reverses arrow direction in RTL", async () => {
    const user = userEvent.setup();
    wrap(<T />, "fa");
    screen.getByRole("tab", { name: "One" }).focus();
    await user.keyboard("{ArrowLeft}");
    expect(screen.getByRole("tab", { name: "Two" })).toHaveAttribute("aria-selected", "true");
  });
});

describe("Modal", () => {
  it("traps focus, closes on Escape and restores focus", async () => {
    const user = userEvent.setup();
    function M() {
      const [open, setOpen] = useState(false);
      return <><button onClick={() => setOpen(true)}>Open</button><Modal open={open} onClose={() => setOpen(false)} title="Confirm" closeLabel="Close" actions={<button>Yes</button>}><p>Body</p></Modal></>;
    }
    wrap(<M />);
    const opener = screen.getByRole("button", { name: "Open" });
    await user.click(opener);
    const dialog = screen.getByRole("dialog", { name: "Confirm" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    const close = within(dialog).getByRole("button", { name: "Close" });
    expect(close).toHaveFocus();
    await user.tab(); await user.tab(); await user.tab();
    expect(dialog.contains(document.activeElement)).toBe(true);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(opener).toHaveFocus();
  });
});

describe("Toast", () => {
  it("announces messages politely and offers an action", async () => {
    const user = userEvent.setup();
    const undo = vi.fn();
    function T() { const toast = useToast(); return <button onClick={() => toast.show({ message: "Removed", action: { label: "Undo", onClick: undo } })}>go</button>; }
    wrap(<T />);
    await user.click(screen.getByText("go"));
    expect(screen.getByRole("status")).toHaveAttribute("aria-live", "polite");
    await user.click(screen.getByRole("button", { name: "Undo" }));
    expect(undo).toHaveBeenCalled();
  });
});

describe("Table", () => {
  it("renders headers with scope and calls onRowClick", async () => {
    const user = userEvent.setup();
    const on = vi.fn();
    Object.defineProperty(window, "innerWidth", { value: 1200, configurable: true });
    wrap(<Table caption="People" rows={[{ id: "1", n: "Ann" }]} rowKey={(r) => r.id} onRowClick={on} columns={[{ id: "n", header: "Name", primary: true, cell: (r) => r.n }]} />);
    expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute("scope", "col");
    await user.click(screen.getByText("Ann"));
    expect(on).toHaveBeenCalled();
  });
  it("turns into cards on compact screens", () => {
    Object.defineProperty(window, "innerWidth", { value: 390, configurable: true });
    wrap(<Table caption="People" rows={[{ id: "1", n: "Ann" }]} rowKey={(r) => r.id} columns={[{ id: "n", header: "Name", cell: (r) => r.n }]} />);
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.getByRole("list", { name: "People" })).toBeInTheDocument();
    Object.defineProperty(window, "innerWidth", { value: 1024, configurable: true });
  });
});

describe("feedback components", () => {
  it("AlertCard uses role=alert for danger and status otherwise", () => {
    wrap(<><AlertCard tone="danger" title="Bad" /><AlertCard tone="info" title="FYI" /></>);
    expect(screen.getByRole("alert")).toHaveTextContent("Bad");
    expect(screen.getAllByRole("status").some((el) => el.textContent?.includes("FYI"))).toBe(true);
  });
  it("ProgressRing is a labelled progressbar clamped to 0-100", () => {
    wrap(<ProgressRing value={140} label="Adherence" />);
    expect(screen.getByRole("progressbar", { name: "Adherence" })).toHaveAttribute("aria-valuenow", "100");
  });
});

describe("healthcare cards", () => {
  it("MedicationCard shows the hierarchy and a DEMO DATA marker", async () => {
    const s = createMockServices({ latencyMs: 0 });
    const meds = await s.medications.forPatient("pt-sara");
    wrap(<MedicationCard med={meds[0]!} variant="detail" />);
    expect(screen.getByRole("heading", { level: 3 })).toHaveTextContent("Demopril");
    expect(screen.getByText("Active ingredient", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("Instructions")).toBeInTheDocument();
    expect(screen.getByText(/Warning \(fictional\)/)).toBeInTheDocument();
    expect(screen.getAllByTestId("demo-badge").length).toBeGreaterThan(0);
  });
  it("PatientCard shows risk with text (not colour alone)", async () => {
    const p = await createMockServices({ latencyMs: 0 }).patients.get("pt-3");
    wrap(<PatientCard patient={p} />);
    expect(screen.getByText("High risk")).toBeInTheDocument();
  });
});

describe("providers", () => {
  it("switching locale updates <html lang dir>", () => {
    function L() { return <AppProviders locale="fa"><span>x</span></AppProviders>; }
    render(<L />);
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.documentElement.lang).toBe("fa");
    cleanup();
    render(<AppProviders locale="en"><span>x</span></AppProviders>);
    expect(document.documentElement.dir).toBe("ltr");
  });
  it("theme provider sets data-theme", () => {
    render(<AppProviders theme="dark"><span>x</span></AppProviders>);
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    act(() => undefined);
    fireEvent.click(document.body);
  });
});
