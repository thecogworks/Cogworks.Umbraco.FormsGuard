import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from "@umbraco-cms/backoffice/extension-api";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { client } from "../api/client.gen.js";

let validationInterceptorAdded = false;

/**
 * The settings API answers 400 with `{ errors: string[] }`. Umbraco's default error interceptor replaces any body
 * that is not ProblemDetails with a generic one, which would drop that list. Reshape it into a ProblemDetails-like
 * body first so `errors` survives onto the thrown error. Must be registered before `configureClient`.
 */
function addValidationErrorInterceptor() {
  if (validationInterceptorAdded) return;
  validationInterceptorAdded = true;

  client.interceptors.response.use(async (response) => {
    if (response.status !== 400) return response;
    try {
      const body = (await response.clone().json()) as { errors?: unknown; type?: unknown };
      if (body?.type !== undefined || !Array.isArray(body?.errors)) return response;
      const errors = body.errors.filter((e): e is string => typeof e === "string");
      const headers = new Headers(response.headers);
      headers.set("Content-Type", "application/json");
      return new Response(JSON.stringify({ type: "ValidationError", title: "Bad Request", status: 400, errors }), {
        status: response.status,
        statusText: response.statusText,
        headers,
      });
    } catch {
      return response;
    }
  });
}

// Give the generated Forms Guard client the backoffice base URL, bearer token and interceptors.
export const onInit: UmbEntryPointOnInit = async (host) => {
  addValidationErrorInterceptor();
  const authContext = await host.getContext(UMB_AUTH_CONTEXT);
  authContext?.configureClient(client);
};

export const onUnload: UmbEntryPointOnUnload = () => {};
