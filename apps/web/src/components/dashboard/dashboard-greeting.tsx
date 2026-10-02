"use client";

import { useEffect, useState } from "react";

type DashboardGreetingProps = {
  tenantName: string;
};

function getGreeting(hour: number) {
  if (hour < 12) return "Buenos días,";
  if (hour < 19) return "Buenas tardes,";
  return "Buenas noches,";
}

export function DashboardGreeting({ tenantName }: DashboardGreetingProps) {
  const [greeting, setGreeting] = useState("Buenos días,");

  useEffect(() => {
    setGreeting(getGreeting(new Date().getHours()));
  }, []);

  return (
    <div className="flex flex-col">
      <span className="text-[11px] font-medium tracking-tight text-[#788073]">{greeting}</span>
      <span className="font-heading text-xl font-semibold lowercase tracking-[-0.03em] text-[#11150e]">{tenantName}</span>
    </div>
  );
}
