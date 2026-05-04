export class HttpError extends Error {
  status: number;
  statusText: string;
  url: string;
  constructor(status: number, statusText: string, url: string) {
    super(`${status} ${statusText} (${url})`);
    this.name = "HttpError";
    this.status = status;
    this.statusText = statusText;
    this.url = url;
  }
}
