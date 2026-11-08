import { Link, Outlet, useRouterState } from "@tanstack/react-router";
import {
  IconFlag,
  IconLogout,
  IconScan,
  IconMicrophone,
  IconSettings,
  IconUserShield,
} from "@tabler/icons-react";
import { useState } from "react";
import {
  AppShell as UiAppShell,
  MenuItem,
  Nav,
  ProfileMenu,
  ThemeMenuItem,
  menuItemClass,
} from "@pluscosmic/ui";
import { UploadsProvider } from "@/components/AddClips/UploadsProvider";
import { UploadTray } from "@/components/AddClips/UploadTray";
import { BinProvider } from "@/components/Reelshelf/BinToast";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { EmailOnboarding } from "@/components/Reelshelf/EmailOnboarding";
import { UsernameOnboarding } from "@/components/Reelshelf/UsernameOnboarding";
import { PlanMeter } from "@/components/Reelshelf/PlanMeter";
import { shellCopy } from "@/copy/shell";
import { useLogout, usePlan } from "@/hooks/queries";
import type { CurrentUser } from "@/shared/services/user";

/**
 * The signed-in app's chrome. It is loaded lazily from the root route so the landing page, which is
 * all a signed-out visitor sees, doesn't wait on the upload machinery and the rest of the library.
 */
export function AppShell({ user }: { user: CurrentUser }) {
  // The plan is fetched only once the menu that shows it opens.
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const plan = usePlan(profileMenuOpen);
  const logout = useLogout();

  const active = (path: string) =>
    path === "/" ? pathname === "/" : pathname.startsWith(path);

  const adminLinks = [
    {
      to: "/legend-detection",
      icon: <IconScan size={16} />,
      label: shellCopy.profileMenu.legendDetection,
    },
    {
      to: "/clip-transcription",
      icon: <IconMicrophone size={16} />,
      label: shellCopy.profileMenu.transcription,
    },
    {
      to: "/admin/accounts",
      icon: <IconUserShield size={16} />,
      label: shellCopy.profileMenu.accounts,
    },
    {
      to: "/admin/reports",
      icon: <IconFlag size={16} />,
      label: shellCopy.profileMenu.reports,
    },
  ] as const;

  return (
    <UiAppShell
      brand={
        <Link to="/" className="rs-brand" aria-label={shellCopy.brandLink}>
          <BrandLogo />
        </Link>
      }
      nav={
        <Nav>
          <Link to="/" className={active("/") ? "active" : undefined}>
            {shellCopy.nav.library}
          </Link>
          <Link
            to="/collections"
            className={active("/collections") ? "active" : undefined}
          >
            {shellCopy.nav.collections}
          </Link>
        </Nav>
      }
      actions={
        <ProfileMenu
          name={user.globalName ?? user.username}
          src={user.avatar}
          onOpenChange={setProfileMenuOpen}
          header={plan.data ? <PlanMeter plan={plan.data} compact /> : null}
        >
          <Link to="/settings" className={menuItemClass} role="menuitem">
            <IconSettings size={16} />
            {shellCopy.profileMenu.settings}
          </Link>
          {user.isAdmin
            ? adminLinks.map((link) => (
                <Link
                  key={link.to}
                  to={link.to}
                  className={menuItemClass}
                  role="menuitem"
                >
                  {link.icon}
                  {link.label}
                </Link>
              ))
            : null}
          <ThemeMenuItem />
          <MenuItem onClick={() => logout.mutate()}>
            <IconLogout size={16} />
            {shellCopy.profileMenu.signOut}
          </MenuItem>
        </ProfileMenu>
      }
    >
      <UploadsProvider>
        <BinProvider>
          {user.needsUsername ? (
            <UsernameOnboarding user={user} />
          ) : user.needsOnboarding ? (
            <EmailOnboarding user={user} />
          ) : (
            <Outlet />
          )}
        </BinProvider>
        <UploadTray />
      </UploadsProvider>
    </UiAppShell>
  );
}
