// Lightweight, theme-aware inline-SVG charts (no dependency). Every color reads a
// design token, so they track light/dark automatically. Sized via viewBox so they
// scale fluidly to their container width.

export interface Segment {
  label: string
  value: number
  color: string
}

/** A tiny inline trend line for stat cards. Scales to its box via viewBox. */
export function Sparkline({
  data,
  className,
  strokeClass = 'text-primary',
}: {
  data: number[]
  className?: string
  strokeClass?: string
}) {
  const w = 120
  const h = 34
  const pad = 3
  const max = Math.max(...data)
  const min = Math.min(...data)
  const span = max - min || 1
  const step = (w - pad * 2) / (data.length - 1)
  const pts = data.map((v, i) => {
    const x = pad + i * step
    const y = pad + (1 - (v - min) / span) * (h - pad * 2)
    return [x, y] as const
  })
  const line = pts.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join(' ')
  const area = `${line} L${pts[pts.length - 1][0].toFixed(1)} ${h} L${pts[0][0].toFixed(1)} ${h} Z`
  const gid = `spark-${Math.random().toString(36).slice(2, 8)}`

  return (
    <svg viewBox={`0 0 ${w} ${h}`} className={className} preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="currentColor" stopOpacity="0.22" />
          <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
        </linearGradient>
      </defs>
      <g className={strokeClass}>
        <path d={area} fill={`url(#${gid})`} stroke="none" />
        <path d={line} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" />
      </g>
    </svg>
  )
}

// Cardinal-spline smoothing for a premium curve.
function smoothPath(pts: readonly (readonly [number, number])[]): string {
  if (pts.length < 2) return ''
  const d: string[] = [`M${pts[0][0]} ${pts[0][1]}`]
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] ?? pts[i]
    const p1 = pts[i]
    const p2 = pts[i + 1]
    const p3 = pts[i + 2] ?? p2
    const c1x = p1[0] + (p2[0] - p0[0]) / 6
    const c1y = p1[1] + (p2[1] - p0[1]) / 6
    const c2x = p2[0] - (p3[0] - p1[0]) / 6
    const c2y = p2[1] - (p3[1] - p1[1]) / 6
    d.push(`C${c1x.toFixed(1)} ${c1y.toFixed(1)} ${c2x.toFixed(1)} ${c2y.toFixed(1)} ${p2[0].toFixed(1)} ${p2[1].toFixed(1)}`)
  }
  return d.join(' ')
}

/** Smooth area chart with gridlines + x labels; brand-colored line + gradient fill. */
export function AreaChart({
  data,
  labels,
  formatValue,
  height = 240,
}: {
  data: number[]
  labels: string[]
  formatValue?: (v: number) => string
  height?: number
}) {
  const w = 760
  const h = height
  const padL = 8
  const padR = 8
  const padT = 16
  const padB = 26
  const max = Math.max(...data)
  const niceMax = max <= 0 ? 1 : Math.ceil(max / 4) * 4
  const step = (w - padL - padR) / (data.length - 1)
  const yOf = (v: number) => padT + (1 - v / niceMax) * (h - padT - padB)
  const pts = data.map((v, i) => [padL + i * step, yOf(v)] as const)
  const line = smoothPath(pts)
  const area = `${line} L${pts[pts.length - 1][0].toFixed(1)} ${h - padB} L${pts[0][0].toFixed(1)} ${h - padB} Z`
  const grid = [0, 0.25, 0.5, 0.75, 1].map((f) => padT + f * (h - padT - padB))
  const last = pts[pts.length - 1]
  const gid = `area-${Math.random().toString(36).slice(2, 8)}`

  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="w-full" role="img" style={{ height: 'auto' }}>
      <defs>
        <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--primary)" stopOpacity="0.20" />
          <stop offset="100%" stopColor="var(--primary)" stopOpacity="0" />
        </linearGradient>
      </defs>
      {grid.map((y, i) => (
        <line key={i} x1={padL} x2={w - padR} y1={y} y2={y} stroke="var(--border-color)" strokeWidth={1} />
      ))}
      <path d={area} fill={`url(#${gid})`} />
      <path d={line} fill="none" stroke="var(--primary)" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={last[0]} cy={last[1]} r={4.5} fill="var(--primary)" stroke="var(--card)" strokeWidth={2.5} />
      {labels.map((lab, i) =>
        i % 2 === 0 ? (
          <text
            key={i}
            x={padL + i * step}
            y={h - 8}
            textAnchor="middle"
            fontSize={11}
            fill="var(--muted-foreground)"
          >
            {lab}
          </text>
        ) : null,
      )}
      {formatValue && (
        <text x={padL} y={12} fontSize={11} fill="var(--muted-foreground)">
          {formatValue(niceMax)}
        </text>
      )}
    </svg>
  )
}

/** Donut with a centered total. Segments are drawn as dash-offset arcs. */
export function Donut({
  segments,
  size = 168,
  thickness = 22,
  centerLabel,
  centerSub,
}: {
  segments: Segment[]
  size?: number
  thickness?: number
  centerLabel?: string
  centerSub?: string
}) {
  const total = segments.reduce((s, x) => s + x.value, 0) || 1
  const r = (size - thickness) / 2
  const c = 2 * Math.PI * r
  let offset = 0

  return (
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img">
      <g transform={`rotate(-90 ${size / 2} ${size / 2})`}>
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          stroke="var(--muted-color)"
          strokeWidth={thickness}
        />
        {segments.map((seg, i) => {
          const len = (seg.value / total) * c
          const el = (
            <circle
              key={i}
              cx={size / 2}
              cy={size / 2}
              r={r}
              fill="none"
              stroke={seg.color}
              strokeWidth={thickness}
              strokeDasharray={`${len} ${c - len}`}
              strokeDashoffset={-offset}
              strokeLinecap="butt"
            />
          )
          offset += len
          return el
        })}
      </g>
      {centerLabel && (
        <text
          x="50%"
          y="47%"
          textAnchor="middle"
          fontSize={22}
          fontWeight={700}
          fill="var(--foreground)"
          style={{ fontVariantNumeric: 'tabular-nums' }}
        >
          {centerLabel}
        </text>
      )}
      {centerSub && (
        <text x="50%" y="62%" textAnchor="middle" fontSize={11} fill="var(--muted-foreground)">
          {centerSub}
        </text>
      )}
    </svg>
  )
}

/** Legend rows for a donut/segment set: swatch · label · value. */
export function Legend({
  segments,
  formatValue,
}: {
  segments: Segment[]
  formatValue?: (v: number) => string
}) {
  return (
    <ul className="flex flex-col gap-2.5">
      {segments.map((s, i) => (
        <li key={i} className="flex items-center gap-2.5 text-sm">
          <span className="h-2.5 w-2.5 shrink-0 rounded-[3px]" style={{ background: s.color }} />
          <span className="text-muted-foreground">{s.label}</span>
          <span className="ml-auto font-semibold tabular-nums text-foreground">
            {formatValue ? formatValue(s.value) : s.value}
          </span>
        </li>
      ))}
    </ul>
  )
}
