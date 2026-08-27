/**
 * The session shapes the API returns. Mirrors `contracts/auth-api.yaml`; nothing here is inferred
 * from a provider token, because the application never sees one.
 */

export interface OrganizationScope {
  departmentId: string | null;
  branchId: string | null;
  teamId: string | null;
}

export interface AuthUser {
  id: string;
  displayName: string;
  email: string;
  population: string;
  permissions: readonly string[];
  scope: OrganizationScope | null;
}

export interface SessionResponse {
  accessToken: string;
  expiresInSeconds: number;
  user: AuthUser;
}

/**
 * Two independent choices, which is the point: ending the CRM session is not negotiable, while
 * ending the provider session also signs the person out of every other corporate application on
 * this computer - so they ask for it explicitly (spec FR-021).
 */
export interface SignOutOptions {
  allSessions?: boolean;
  endProviderSession?: boolean;
}

export interface SignOutResponse {
  signedOut: boolean;
  providerSignOutUrl: string | null;
}
