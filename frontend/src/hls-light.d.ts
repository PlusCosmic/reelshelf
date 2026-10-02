// hls.js ships types for its full build only; the light build has the same API minus features clips don't use.
declare module "hls.js/light" {
  export * from "hls.js";
  export { default } from "hls.js";
}
