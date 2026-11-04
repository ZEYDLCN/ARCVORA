import { createContext, useContext } from 'react';

export const ThemeContext = createContext<{ dark: boolean }>({ dark: false });
export const useTheme = () => useContext(ThemeContext);
