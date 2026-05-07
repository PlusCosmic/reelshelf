export interface Project {
  title: string;
  subtitle: string;
  description: string;
  badges: string[];
  path: string;
  githubUrl: string;
  liveUrl?: string;
  overview: string;
  features: string[];
  highlights: string[];
  architecture: string;
}

export const projects: Project[] = [
  {
    title: "Clips",
    subtitle: "Video clip management platform",
    description:
      "A full-stack platform for uploading, transcoding, and managing video game clips. Built with a React/TypeScript frontend and C# .NET backend, with Bunny CDN for delivery and resumable uploads via the tus protocol.",
    badges: ["React", "TypeScript", "C#", ".NET"],
    path: "/projects/clips",
    githubUrl: "https://github.com/PlusCosmic/plus-cosmic-dev",
    overview:
      "Clips is a personal video clip management platform I built to organise and share gameplay highlights. The frontend is a React/TypeScript SPA using TanStack Router for file-based routing, and the backend is a C#/.NET API backed by PostgreSQL. Videos are uploaded via the tus protocol for resumable transfers, then transcoded and delivered through Bunny CDN. The app supports category and tag management, view tracking, and generates blurhash placeholders so pages feel snappy while videos load.",
    features: [
      "Resumable video uploads via the tus protocol",
      "Automatic transcoding and CDN delivery via Bunny",
      "Category and tag management for organising clips",
      "Blurhash placeholder generation for video thumbnails",
      "View tracking and analytics",
      "File hashing to prevent duplicate uploads",
    ],
    highlights: [
      "Implemented resumable uploads so large video files survive network interruptions — critical for multi-gigabyte gameplay recordings",
      "Built file hashing on the client side to detect and prevent duplicate uploads before they hit the server",
      "Custom video player with CDN-optimised delivery, supporting adaptive quality",
    ],
    architecture:
      "React/TypeScript frontend with TanStack Router, C#/.NET backend API, PostgreSQL database, Bunny CDN for video storage and delivery",
  },
  {
    title: "Star Battles",
    subtitle: "Puzzle solver and generator",
    description:
      "A Star Battle puzzle solver and generator built in Go, featuring constraint propagation algorithms, a terminal UI built with Bubbletea, and a web frontend in React/Mantine.",
    badges: ["Go", "React", "TypeScript"],
    path: "/projects/star-battles",
    githubUrl: "https://github.com/PlusCosmic/star-battles",
    overview:
      "Star Battles is a puzzle solver and generator for the Star Battle logic puzzle. The core solver is written in Go and uses constraint propagation to solve puzzles efficiently without resorting to brute-force backtracking. The project includes both a terminal UI built with Bubbletea for a polished CLI experience and a web frontend in React/Mantine. Both interfaces share the same Go solver engine, with the web version communicating via an API.",
    features: [
      "Puzzle solver using constraint propagation",
      "Puzzle generator with configurable difficulty",
      "Terminal UI built with Bubbletea",
      "Web frontend in React/Mantine",
      "Shared Go solver engine across both interfaces",
    ],
    highlights: [
      "Constraint propagation algorithm that solves puzzles without brute-force backtracking — significantly faster and more elegant than a naive approach",
      "Dual interface — terminal TUI and web app — both powered by the same Go solver engine",
      "Bubbletea-based TUI with a polished, interactive terminal experience",
    ],
    architecture:
      "Go backend with solver engine, React/Mantine web frontend, Bubbletea terminal UI",
  },
];
