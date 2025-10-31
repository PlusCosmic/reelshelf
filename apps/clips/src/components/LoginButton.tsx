import { Button, type ButtonProps } from "@mantine/core";
import { DiscordIcon } from "@mantinex/dev-icons";
import * as React from "react";
import classes from "./LoginButton.module.scss";
import { apiConfig } from "../config/apiConfig.ts";

export default function LoginButton() {
  function handleLogin() {
    const currentUrl = window.location.href;
    window.location.href = `${apiConfig.baseUrl}/auth/discord/login?returnUrl=${encodeURIComponent(currentUrl)}`;
  }

  return (
    <DiscordButton onClick={handleLogin}>Log in with Discord</DiscordButton>
  );
}

export function DiscordButton(
  props: ButtonProps & React.ComponentPropsWithoutRef<"button">,
) {
  return (
    <Button maw="200"
      className={classes.discordButton}
      leftSection={<DiscordIcon size={16} />}
      {...props}
    />
  );
}
