import { useEffect } from 'react';
import { APP_NAME } from '../constants';

export function usePageTitle(title: string) {
  useEffect(() => {
    document.title = `${title} - ${APP_NAME}`;
  }, [title]);
}
