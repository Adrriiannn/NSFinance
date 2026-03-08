import { apiRequest } from "./api/client";
import type { ApiHealth } from "../types/health";

export async function fetchApiHealth(): Promise<ApiHealth> {
  return apiRequest<ApiHealth>("/health");
}
