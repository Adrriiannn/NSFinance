import { apiRequest } from "../../lib/api/client";
import type { CategoryDto } from "../../types/api";

export function getCategories(): Promise<CategoryDto[]> {
  return apiRequest<CategoryDto[]>("/api/categories");
}

