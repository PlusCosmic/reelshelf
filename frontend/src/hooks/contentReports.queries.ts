import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  dismissContentReport,
  listContentReports,
  reportSharedClip,
  takeDownContentReport,
  type ReportReason,
} from "@/shared/services/contentReports";

export const contentReportsQueryKey = ["content-reports"] as const;

export function useReportSharedClip(token: string) {
  return useMutation({
    mutationFn: (report: {
      reason: ReportReason;
      details: string;
      email: string;
    }) => reportSharedClip(token, report),
  });
}

export function useContentReports(resolved: boolean) {
  return useQuery({
    queryKey: [...contentReportsQueryKey, resolved ? "resolved" : "open"],
    queryFn: () => listContentReports(resolved),
    retry: false,
  });
}

/** Takes the report's clip down or dismisses the report, then refreshes both lists. */
export function useResolveContentReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      reportId,
      action,
      note,
    }: {
      reportId: string;
      action: "take-down" | "dismiss";
      note: string;
    }) =>
      action === "take-down"
        ? takeDownContentReport(reportId, note)
        : dismissContentReport(reportId, note),
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: contentReportsQueryKey }),
  });
}
