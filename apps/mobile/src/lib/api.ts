import { apiBaseUrl } from "./config";
import type { ApiHealth } from "../types/health";

export async function fetchApiHealth(): Promise<ApiHealth> {
  const response = await fetch(`${apiBaseUrl}/health`);

  if (!response.ok) {
    throw new Error(`Health request failed: ${response.status}`);
  }

  return (await response.json()) as ApiHealth;
}
