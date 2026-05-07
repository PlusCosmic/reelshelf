import type { UploadStatus } from "@/hooks/useClipUpload";

export function UploadHeader({
  status,
  step,
}: {
  status: UploadStatus;
  step: number;
}) {
  return (
    <>
      <div className="rs-eyebrow">Step {step} of 3 - Upload</div>
      <h1 className="rs-display rs-upload-title">
        {step === 1 ? (
          <>
            Drop a clip <em>onto the shelf</em>.
          </>
        ) : step === 2 ? (
          <>Sending the clip...</>
        ) : status === "done" ? (
          <>
            Uploaded. <em>Ready for the shelf.</em>
          </>
        ) : (
          <>
            Almost there. <em>Check the details.</em>
          </>
        )}
      </h1>

      <div className="rs-upload-steps" aria-label={`Step ${step} of 3`}>
        {[1, 2, 3].map((item) => (
          <span key={item} className={item <= step ? "active" : undefined} />
        ))}
      </div>
    </>
  );
}
