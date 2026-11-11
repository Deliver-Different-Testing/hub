import { QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { queryClient } from './api/queryClient';
import RequireAuth from './components/RequireAuth';
import UnauthorizedRedirect from './components/UnauthorizedRedirect';
import { ColorModeProvider } from './hooks/useColorMode';
import AgentAccessConnectPage from './pages/AgentAccessConnectPage';
import AgentAccessUnavailablePage from './pages/AgentAccessUnavailablePage';
import ForgotPasswordPage from './pages/ForgotPasswordPage';
import FuelSurchargePage from './pages/FuelSurchargePage';
import HomePage from './pages/HomePage';
import LoginPage from './pages/LoginPage';
import ResetPasswordPage from './pages/ResetPasswordPage';
import SettingsPage from './pages/SettingsPage';
import ThemedApp from './ThemedApp';

export function AppRoutes() {
  return (
    <>
      <UnauthorizedRedirect />
      <Routes>
        <Route path="/Account/Login" element={<LoginPage />} />
        <Route path="/Account/ForgotPassword" element={<ForgotPasswordPage />} />
        <Route path="/Account/ResetPassword" element={<ResetPasswordPage />} />
        {/* Connecting an AI app: a card on its own like the login page, not inside the app shell. */}
        <Route path="/agent-access/connect" element={<AgentAccessConnectPage />} />
        <Route path="/agent-access/unavailable" element={<AgentAccessUnavailablePage />} />
        <Route element={<RequireAuth />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/Account/Settings" element={<SettingsPage />} />
          <Route path="/FuelSurcharge" element={<FuelSurchargePage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ColorModeProvider>
        <ThemedApp>
          <BrowserRouter>
            <AppRoutes />
          </BrowserRouter>
        </ThemedApp>
      </ColorModeProvider>
    </QueryClientProvider>
  );
}
