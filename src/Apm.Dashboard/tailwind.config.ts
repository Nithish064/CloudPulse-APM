import type { Config } from "tailwindcss";

const config: Config = {
  darkMode: "class",
  content: [
    "./pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        darkbg: "#0b0f19",
        darkcard: "#111827",
        darkborder: "#1f2937",
        pulse: {
          500: "#22c55e",
          600: "#16a34a"
        }
      }
    }
  },
  plugins: []
};

export default config;
