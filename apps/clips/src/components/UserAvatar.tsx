import { useEffect, useState } from "react";
import { IconLogout } from "@tabler/icons-react";
import { fetchMe } from "../services/user";
import { logout } from "../services/auth";
import LoginButton from "./LoginButton";
import { Avatar, Menu, Skeleton } from "@mantine/core";
import type { DiscordUser } from "@pluscosmic/nucleus-api-client";

export default function UserAvatar() {
  const [user, setUser] = useState<DiscordUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let isMounted = true;
    setLoading(true);
    (async () => {
      try {
        const me = await fetchMe();
        if (!isMounted) return;
        setUser(me);
      } catch (e) {
        if (!isMounted) return;
        console.error(e);
      } finally {
        if (isMounted) setLoading(false);
      }
    })();
    return () => {
      isMounted = false;
    };
  }, []);

  async function handleLogout() {
    try {
      await logout();
    } catch (e) {
      console.error("Logout failed", e);
    } finally {
      setUser(null);

      window.location.reload();
    }
  }

  if (!user) {
    return <LoginButton />;
  }

  return (
    <div>
      {loading && <Skeleton height={50} circle mb="xl" />}
      {!loading && user && (
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
                  variant="filled"
                  radius="xl"
                  color="green"
                />
              )}
            </div>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item
              leftSection={<IconLogout size={14} />}
              onClick={handleLogout}
            >
              Log Out
            </Menu.Item>
          </Menu.Dropdown>
        </Menu>
      )}
    </div>
  );
}
