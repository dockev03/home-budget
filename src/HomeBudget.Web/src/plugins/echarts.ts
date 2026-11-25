import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { BarChart, LineChart, PieChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import 'vue-echarts/style.css'

// Seuls les modules utilisés sont embarqués (anneau, barres, courbe), pour alléger la PWA.
use([CanvasRenderer, BarChart, LineChart, PieChart, GridComponent, LegendComponent, TooltipComponent])

export { default as VChart } from 'vue-echarts'
