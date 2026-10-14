import React from 'react';
import { ServerCog, ShieldCheck } from 'lucide-react';

export function LoginLayout({
  title,
  description,
  footer,
  embedded = false,
  children,
}: {
  title: string;
  description: string;
  footer: string;
  embedded?: boolean;
  children: React.ReactNode;
}) {
  const Container = embedded ? 'section' : 'main';
  return (
    <Container className={embedded ? 'login-panel' : 'login-page'}>
      <div className="login-card">
        <div className="brand">
          <ServerCog aria-hidden="true" />
          <span>PalworldPanel</span>
        </div>
        <h1>{title}</h1>
        <p className="muted">{description}</p>
        {children}
        <p className="security-note">
          <ShieldCheck size={16} aria-hidden="true" /> {footer}
        </p>
      </div>
    </Container>
  );
}
