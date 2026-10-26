import type { AdminContentReport } from "@/api-client";
import { createContentReportApi } from "./apiClients";

export type { AdminContentReport };

/** The reasons a shared clip can be reported for, as the API spells them, in the order the form lists them. */
export const reportReasons = [
  { value: "child_safety", label: "Child sexual abuse or exploitation" },
  { value: "terrorism", label: "Terrorism or violent extremism" },
  { value: "sexual", label: "Nudity or sexual content" },
  { value: "violence", label: "Graphic violence or gore" },
  { value: "hate_or_harassment", label: "Hate or harassment" },
  { value: "copyright", label: "It uses my work without permission" },
  { value: "other", label: "Something else that breaks the terms" },
] as const;

export type ReportReason = (typeof reportReasons)[number]["value"];

export function reportReasonLabel(reason: string): string {
  return reportReasons.find((item) => item.value === reason)?.label ?? reason;
}

export async function reportSharedClip(
  token: string,
  report: { reason: ReportReason; details: string; email: string },
): Promise<void> {
  await createContentReportApi().reportSharedClip({
    token,
    contentReportRequest: {
      reason: report.reason,
      details: report.details.trim() || null,
      email: report.email.trim() || null,
    },
  });
}

/** Open reports oldest first, or the latest dealt with. */
export async function listContentReports(
  resolved: boolean,
): Promise<AdminContentReport[]> {
  return createContentReportApi().listContentReports({ resolved });
}

export async function takeDownContentReport(
  reportId: string,
  note: string,
): Promise<AdminContentReport> {
  return createContentReportApi().takeDownContentReport({
    reportId,
    resolveContentReportRequest: { note },
  });
}

export async function dismissContentReport(
  reportId: string,
  note: string,
): Promise<AdminContentReport> {
  return createContentReportApi().dismissContentReport({
    reportId,
    resolveContentReportRequest: { note: note.trim() || null },
  });
}
