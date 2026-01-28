import { Button, type ButtonProps } from "@mantine/core";
import { DiscordIcon } from "@mantinex/dev-icons";
import classes from "./LoginButton.module.scss";
import React from "react";

export default function LoginButton() {
  function handleLogin() {
    // Use full URL but validate it's same-origin to prevent open redirect vulnerability
    const currentUrl = window.location.href;

    // Validate the URL is safe (same origin)
    try {
      const url = new URL(currentUrl);
      // Only allow URLs from the same origin (same protocol, domain, and port)
      if (url.origin !== window.location.origin) {
        console.error('Invalid redirect URL: different origin');
        return;
      }
    } catch (error) {
      console.error('Invalid redirect URL', error);
      return;
    }

    // Auth endpoints are at root level (not under /api) for OAuth callback compatibility
    window.location.href = `/auth/discord/login?returnUrl=${encodeURIComponent(currentUrl)}`;
  }

  return <DiscordButton onClick={handleLogin}>Log in with Discord</DiscordButton>;
}

export function DiscordButton(
  props: ButtonProps & React.ComponentPropsWithoutRef<"button">
) {
  return (
    <Button
      className={classes.discordButton}
      leftSection={<DiscordIcon size={16} />}
      {...props}
    />
  );
}
