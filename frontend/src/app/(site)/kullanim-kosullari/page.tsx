import { LegalPage, legalMetadata } from "@/components/site/legal-page";
import { legalDocs } from "@/content/legal";

export const metadata = legalMetadata(legalDocs.kosullar);

export default function TermsPage() {
  return <LegalPage doc={legalDocs.kosullar} />;
}
