export function PanelNotices({
  actionError,
  statusError,
  onDismissAction,
}: {
  actionError: string;
  statusError: string;
  onDismissAction: () => void;
}) {
  return (
    <>
      {actionError && (
        <div className="error-box" role="alert">
          {actionError}
          <button className="ghost" onClick={onDismissAction}>
            收起
          </button>
        </div>
      )}
      {statusError && (
        <p className="error-box" role="status">
          采样未更新：{statusError}
        </p>
      )}
    </>
  );
}
