import { useMemo } from "react";
import { useAuth } from "../auth/AuthContext";
import { MedicationApi } from "./medicationApi";

export function useMedicationApi(): MedicationApi {
  const { backend } = useAuth();
  return useMemo(() => new MedicationApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
}
