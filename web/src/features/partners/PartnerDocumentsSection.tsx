import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { DocumentStatus, listSalesDocuments } from '@/lib/api/documents'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

export default function PartnerDocumentsSection({ partnerId }: { partnerId: string }) {
  const { t, i18n } = useTranslation('documents')
  const [page, setPage] = useState(1)
  const pageSize = 5
  const documents = useQuery({
    queryKey: ['documents', { partnerId, page, pageSize }],
    queryFn: () => listSalesDocuments({ partnerId, page, pageSize }),
  })
  const pages = Math.max(1, Math.ceil((documents.data?.total ?? 0) / pageSize))

  return (
    <Card className="md:col-span-2">
      <CardHeader>
        <CardTitle>{t('detail.documents', { ns: 'partners' })}</CardTitle>
      </CardHeader>
      <CardContent>
        {documents.isLoading && <p className="text-sm text-muted-foreground">{t('table.loading')}</p>}
        {documents.isError && <p role="alert" className="text-sm text-destructive">{t('loadError')}</p>}
        {documents.data && (
          <>
            {documents.data.items.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('table.empty')}</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('columns.number')}</TableHead>
                    <TableHead>{t('columns.type')}</TableHead>
                    <TableHead>{t('columns.status')}</TableHead>
                    <TableHead className="text-right">{t('columns.gross')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {documents.data.items.map((document) => (
                    <TableRow key={document.id}>
                      <TableCell>
                        <Link to={`/documents/${document.id}`} className="font-medium text-primary hover:underline">
                          {document.documentNumber ?? t('status.0')}
                        </Link>
                      </TableCell>
                      <TableCell>{t(`type.${document.documentType}`)}</TableCell>
                      <TableCell>{t(`status.${document.status}`)}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {document.status === DocumentStatus.Draft ? '—' : new Intl.NumberFormat(i18n.language, {
                          style: 'currency', currency: document.currency,
                        }).format(document.totalGross)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
            {pages > 1 && (
              <div className="mt-3 flex items-center justify-end gap-3 text-sm">
                <Button variant="outline" size="sm" disabled={page === 1} onClick={() => setPage(page - 1)}>
                  {t('table.prev')}
                </Button>
                <span>{t('table.page', { page, pages })}</span>
                <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                  {t('table.next')}
                </Button>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
