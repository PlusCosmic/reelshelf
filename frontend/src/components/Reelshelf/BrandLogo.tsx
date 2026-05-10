import logoDarkUrl from "@/assets/reelshelf-logo-dark.svg";
import logoLightUrl from "@/assets/reelshelf-logo-light.svg";

export function BrandLogo() {
  return (
    <span className="rs-brand-logo" aria-hidden="true">
      <img
        className="rs-brand-logo-image rs-brand-logo-light"
        src={logoLightUrl}
        alt=""
      />
      <img
        className="rs-brand-logo-image rs-brand-logo-dark"
        src={logoDarkUrl}
        alt=""
      />
    </span>
  );
}
