import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { setNonce } from 'get-nonce';

const styleNonce = document.querySelector<HTMLMetaElement>('meta[name="style-nonce"]')?.content;
if (styleNonce && styleNonce !== '__STYLE_NONCE__') setNonce(styleNonce);

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
