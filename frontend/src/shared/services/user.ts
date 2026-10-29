import type { CurrentUserResponse, LinkedIdentity } from "@/api-client";
import { createUserApi } from "./apiClients";

export interface CurrentUser {
  id: string;
  username: string;
  globalName: string | null;
  avatar: string | null;
  email: string | null;
  suggestedEmail: string | null;
  /** True until the account has an email address and, when it picks its own username, a confirmed one. */
  needsOnboarding: boolean;
  /** The account's username is a stand-in (a Google sign-in has none) that its owner must confirm or change. */
  needsUsername: boolean;
  /** The owner sets the username here; otherwise it follows the primary sign-in. */
  canSetUsername: boolean;
  isAdmin: boolean;
}

export async function fetchMe(): Promise<CurrentUser | null> {
  const user = await createUserApi().getMe();
  return user ? fromCurrentUserResponse(user) : null;
}

export type { LinkedIdentity };

export async function fetchLinkedIdentities(): Promise<LinkedIdentity[]> {
  return createUserApi().getMyLinkedIdentities();
}

export async function unlinkIdentity(provider: string): Promise<void> {
  await createUserApi().unlinkMyIdentity({ provider });
}

/** Stores the address account mail goes to. Every account needs one, so the API refuses an empty one. */
export async function setEmail(email: string): Promise<void> {
  await createUserApi().setMyEmail({ setEmailRequest: { email } });
}

/** Sets a username the owner chooses; the API refuses one another account has (409). */
export async function setUsername(username: string): Promise<void> {
  await createUserApi().setMyUsername({ setUsernameRequest: { username } });
}

/** The phrase the user types to confirm; the API checks it too. */
export const deleteAccountPhrase = "delete my account";

/** Permanently deletes the account and ends the session; clips are removed in the background. */
export async function deleteAccount(confirmation: string): Promise<void> {
  await createUserApi().deleteMyAccount({
    deleteAccountRequest: { confirmation },
  });
}

function fromCurrentUserResponse(user: CurrentUserResponse): CurrentUser {
  return {
    id: user.id,
    username: user.username,
    globalName: user.globalName ?? null,
    avatar: user.avatar ?? null,
    email: user.email ?? null,
    suggestedEmail: user.suggestedEmail ?? null,
    needsOnboarding: user.needsOnboarding,
    needsUsername: user.needsUsername,
    canSetUsername: user.canSetUsername,
    isAdmin: user.isAdmin,
  };
}
