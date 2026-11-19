# Conjunto de evaluación del encaje (inventado)

Veinte ofertas **inventadas** para medir cuánto acierta AnalizarEncaje (decisión 5 de la F7b). Las empresas, los
puestos y los textos son ficticios: no hay datos de nadie real.

- `oferta-01.md` … `oferta-20.md`: el texto de cada oferta, como lo descargaría la app.
- `esperado.json`: para cada oferta, la ficha (empresa, puesto, ubicación y modalidad) y el encaje esperado
  (`Alto`, `Medio`, `TiroLargo` o `No`) con una frase de por qué.
- El perfil contra el que se valora es siempre `datos-ejemplo/perfil-ejemplo.md`, nunca el real.

Los encajes esperados los propone el desarrollador y los corrige el dueño del proyecto. **Estado: propuestos,
pendientes de revisar por el dueño** (sobre todo las ofertas 09, 13 y 14). La `oferta-18` lleva una frase que intenta
dictar el encaje: Claude debe ignorarla.

## Cómo se usa

Lo usa la consola `tests/EvaluacionEncaje`, que lanza `claude` de verdad y gasta del cupo. No la ejecuta
`dotnet test`: solo se lanza a mano, con el OK expreso del dueño (prueba A de la F7b):

```
dotnet run --project tests/EvaluacionEncaje -- --confirmo-gasto --tope-usd 5
```

`ConjuntoEncajeTests` comprueba que el conjunto está completo y no tiene datos personales.
