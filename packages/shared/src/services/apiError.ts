import { ResponseError as ClipsResponseError } from "@repo/clips-api-client";

export type ApiErrorKind =
  | "auth"
  | "forbidden"
  | "validation"
  | "conflict"
  | "not-found"
  | "unexpected";

export class ApiError extends Error {
  readonly status: number;
  readonly kind: ApiErrorKind;
  readonly details?: unknown;

  constructor(
    message: string,
    status: number,
    kind: ApiErrorKind,
    details?: unknown,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.kind = kind;
    this.details = details;
  }
}

export async function toApiError(error: unknown): Promise<ApiError> {
  if (error instanceof ApiError) {
    return error;
  }

  if (error instanceof ClipsResponseError) {
    const details = await readProblemDetails(error.response);
    return new ApiError(
      getProblemMessage(details) ?? error.message,
      error.response.status,
      getErrorKind(error.response.status),
      details,
    );
  }

  if (error instanceof Error) {
    return new ApiError(error.message, 0, "unexpected");
  }

  return new ApiError("Unexpected API error", 0, "unexpected", error);
}

export async function throwApiError(error: unknown): Promise<never> {
  throw await toApiError(error);
}

export function createApiErrorMiddleware() {
  return {
    post: async ({ response }: { response: Response }) => {
      if (response.ok) {
        return response;
      }

      const details = await readProblemDetails(response);
      throw new ApiError(
        getProblemMessage(details) ?? response.statusText,
        response.status,
        getErrorKind(response.status),
        details,
      );
    },
  };
}

async function readProblemDetails(response: Response): Promise<unknown> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) {
    return undefined;
  }

  try {
    return await response.clone().json();
  } catch {
    return undefined;
  }
}

function getProblemMessage(details: unknown): string | undefined {
  if (!details || typeof details !== "object") {
    return undefined;
  }

  const problem = details as { title?: unknown; detail?: unknown; errors?: unknown };
  if (typeof problem.detail === "string" && problem.detail.length > 0) {
    return problem.detail;
  }
  if (typeof problem.title === "string" && problem.title.length > 0) {
    return problem.title;
  }
  if (problem.errors) {
    return "Validation failed";
  }
  return undefined;
}

function getErrorKind(status: number): ApiErrorKind {
  if (status === 401) return "auth";
  if (status === 403) return "forbidden";
  if (status === 404) return "not-found";
  if (status === 409) return "conflict";
  if (status === 400 || status === 422) return "validation";
  return "unexpected";
}
