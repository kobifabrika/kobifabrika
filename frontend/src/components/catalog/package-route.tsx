"use client";

import { useParams } from "next/navigation";

import { PackageEdit } from "./packages";

export function PackageEditRoute() {
  const { id } = useParams<{ id: string }>();
  return <PackageEdit id={id} />;
}
