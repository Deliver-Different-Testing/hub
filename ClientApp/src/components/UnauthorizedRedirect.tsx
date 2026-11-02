import { useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { setUnauthorizedHandler } from '../api/client';
import { isAnonymousPath, loginUrl } from '../constants';

/** A 401 from any API call on an authenticated route sends the user to the login page and back again. */
export default function UnauthorizedRedirect() {
  const navigate = useNavigate();
  const location = useLocation();
  const path = location.pathname + location.search;
  const anonymous = isAnonymousPath(location.pathname);

  useEffect(() => {
    if (anonymous) {
      setUnauthorizedHandler(null);
      return;
    }
    setUnauthorizedHandler(() => navigate(loginUrl(path), { replace: true }));
    return () => setUnauthorizedHandler(null);
  }, [anonymous, navigate, path]);

  return null;
}
