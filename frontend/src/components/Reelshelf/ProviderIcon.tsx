import { IconBrandDiscord, IconBrandTwitch } from "@tabler/icons-react";

export function ProviderIcon({
  provider,
  size = 18,
}: {
  provider: string;
  size?: number;
}) {
  if (provider === "twitch") {
    return <IconBrandTwitch size={size} />;
  }
  return <IconBrandDiscord size={size} />;
}
