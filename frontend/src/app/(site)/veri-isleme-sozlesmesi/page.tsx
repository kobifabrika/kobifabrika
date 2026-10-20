import { LegalPage, legalMetadata } from "@/components/site/legal-page";
import { legalDocs } from "@/content/legal";

export const metadata = legalMetadata(legalDocs.dpa);

export default function DpaPage() {
  return <LegalPage doc={legalDocs.dpa} />;
}
