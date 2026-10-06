import React, { useEffect, useRef, useState } from 'react';
import { ChevronDown } from 'lucide-react';

export function MoreActions({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const id = React.useId();
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setOpen(false);
        trigger.current?.focus();
      }
    };
    document.addEventListener('pointerdown', outside);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('pointerdown', outside);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);
  return (
    <div
      className="more-actions"
      ref={root}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && open) {
          event.preventDefault();
          event.stopPropagation();
          setOpen(false);
          trigger.current?.focus();
        }
      }}
    >
      <button
        ref={trigger}
        className="ghost"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen(!open)}
      >
        更多 <ChevronDown size={15} aria-hidden="true" />
      </button>
      {open && (
        <div
          className="more-actions-panel"
          id={id}
          role="group"
          aria-label="更多实例操作"
          onClickCapture={(event) => {
            if (event.target instanceof Element && event.target.closest('button:not(:disabled)')) {
              setOpen(false);
              // Capture the visible trigger before an action opens its confirmation dialog.
              trigger.current?.focus();
            }
          }}
        >
          {children}
        </div>
      )}
    </div>
  );
}
