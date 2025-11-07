import { Avatar, Menu, Skeleton } from "@mantine/core";
import { IconLogout } from "@tabler/icons-react";
import { LoginButton } from "@repo/ui";
import { useCurrentUser, useLogout } from "../hooks/queries";

interface UserAvatarProps {
  hideLogin: boolean;
}

export default function UserAvatar({ hideLogin }: UserAvatarProps) {
  const { data: user, isLoading } = useCurrentUser();
  const logoutMutation = useLogout();

  async function handleLogout() {
    try {
      await logoutMutation.mutateAsync();
      window.location.reload();
    } catch (e) {
      console.error("Logout failed", e);
      // Still reload to clear local state even if API call failed
      window.location.reload();
    }
  }

  if (!user && !hideLogin) {
    return <LoginButton />;
  }

  return (
    <div>
      {isLoading && <Skeleton height={50} circle mb="xl" />}
      {!isLoading && user && (
        <Menu>
          <Menu.Target>
            <div>
              {user.avatar && (
                <Avatar
                  style={{ cursor: "pointer" }}
                  component="button"
                  src={user.avatar}
                  radius="xl"
                />
              )}
              {!user.avatar && (
                <Avatar
                  style={{ cursor: "pointer" }}
                  component="button"
                  variant="filled"
                  radius="xl"
                  color="green"
                />
              )}
            </div>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item leftSection={<IconLogout size={14} />} onClick={handleLogout}>
              Log Out
            </Menu.Item>
          </Menu.Dropdown>
        </Menu>
      )}
    </div>
  );
}
