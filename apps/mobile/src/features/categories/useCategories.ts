import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import { getCategories } from "./categoriesApi";

export function useCategoriesQuery() {
  return useQuery({
    queryKey: queryKeys.categories.all,
    queryFn: getCategories
  });
}
