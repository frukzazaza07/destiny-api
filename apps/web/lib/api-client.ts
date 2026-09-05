export type ApiEnvelope<T> = {
  success: boolean;
  data: T | null;
  error: string | Record<string, string[]> | null;
  code: string;
};

export function apiUrl(path: string) {
  const configured = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  if (configured) return `${configured.replace(/\/+$/, "")}${path}`;
  return path;
}

export async function apiFetch(path: string, init: RequestInit = {}) {
  return fetch(apiUrl(path), { ...init, credentials: "include" });
}

export async function readApiData<T>(response: Response): Promise<T> {
  const envelope = (await response.json()) as ApiEnvelope<T>;
  if (!response.ok || !envelope.success || envelope.data === null) {
    throw new Error(readEnvelopeError(envelope) ?? "The request could not be completed.");
  }
  return envelope.data;
}

export async function readApiError(response: Response, fallback: string) {
  try {
    const envelope = (await response.json()) as ApiEnvelope<unknown>;
    return readEnvelopeError(envelope) ?? fallback;
  } catch {
    return fallback;
  }
}

export async function csrfToken() {
  const response = await apiFetch("/api/auth/csrf", { cache: "no-store" });
  const data = await readApiData<{ token: string }>(response);
  return data.token;
}

export async function apiMutation(path: string, method: string, body?: unknown) {
  const token = await csrfToken();
  return apiFetch(path, {
    method,
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
}

function readEnvelopeError(envelope: ApiEnvelope<unknown>) {
  if (typeof envelope.error === "string") return envelope.error;
  if (envelope.error && typeof envelope.error === "object") {
    return Object.values(envelope.error).flat().join(" ");
  }
  return null;
}
