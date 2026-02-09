export interface SkillCategory {
  category: string;
  skills: string[];
}

export const skillCategories: SkillCategory[] = [
  {
    category: "Languages",
    skills: ["Java", "TypeScript", "C#", "Go", "Python", "SQL"],
  },
  {
    category: "Frameworks & Libraries",
    skills: ["React", "Spring Boot", ".NET", "WPF", "TanStack Router", "Mantine"],
  },
  {
    category: "Databases",
    skills: ["PostgreSQL", "MySQL", "MongoDB", "Redis"],
  },
  {
    category: "Infrastructure & Tools",
    skills: ["Docker", "RabbitMQ", "Bunny CDN", "Azure DevOps", "Git", "Turborepo", "Vite"],
  },
];
