const fs = require('node:fs');
const path = require('node:path');
const { z } = require('zod');
const { workSchema, resultSchema } = require('../dist/contracts');
const directory = path.resolve(__dirname, '../../../contracts/reading-jobs/v1');
fs.mkdirSync(directory, { recursive: true });
for (const [name, schema] of [['work', workSchema], ['result', resultSchema]])
  fs.writeFileSync(path.join(directory, `${name}.schema.json`), JSON.stringify(z.toJSONSchema(schema), null, 2) + '\n');
