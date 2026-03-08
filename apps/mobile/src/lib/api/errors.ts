import type { ApiErrorResponse, ValidationProblem } from "../../types/api";

export class ApiClientError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly validation?: ValidationProblem;
  readonly details?: unknown;

  constructor(
    message: string,
    status: number,
    options?: {
      code?: string;
      validation?: ValidationProblem;
      details?: unknown;
    }
  ) {
    super(message);
    this.name = "ApiClientError";
    this.status = status;
    this.code = options?.code;
    this.validation = options?.validation;
    this.details = options?.details;
  }
}

export function parseApiErrorBody(value: unknown): {
  message?: string;
  code?: string;
  validation?: ValidationProblem;
} {
  if (!value || typeof value !== "object") {
    return {};
  }

  const maybeValidation = value as ValidationProblem;
  if (maybeValidation.errors && typeof maybeValidation.errors === "object") {
    const firstMessage = Object.values(maybeValidation.errors).flat()[0];
    return {
      message: firstMessage || maybeValidation.message || maybeValidation.title,
      validation: maybeValidation
    };
  }

  const maybeError = value as ApiErrorResponse;
  return {
    message: maybeError.message,
    code: maybeError.code
  };
}

export function formatUnknownError(error: unknown): string {
  if (error instanceof ApiClientError) {
    return error.message;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "An unexpected error occurred.";
}
